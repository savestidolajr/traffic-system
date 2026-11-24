using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArnBulletinGenerator.Infrastructure.Identity;

/// <summary>
///     Reads application-role holders from Microsoft Graph for the operator display surface.
///     Successful snapshots are cached briefly and may be shown stale when Graph is unavailable.
///     The cache never participates in authorization; the Function App's managed identity must
///     have admin-consented <c>Directory.Read.All</c> application permission in the ARN tenant.
/// </summary>
public sealed class EntraOperatorDirectory : IOperatorDirectory
{
    private const string GraphClientName = nameof(EntraOperatorDirectory);
    private const string DefaultAccessRoleId = "00000000-0000-0000-0000-000000000000";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0/";
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];
    private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DirectoryTimeout = TimeSpan.FromSeconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TokenCredential _tokenCredential;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OperatorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _snapshotGate = new();
    private readonly object _loadGate = new();
    private SnapshotCacheEntry? _snapshot;
    private Task<Result<IReadOnlyList<DirectoryPrincipal>>>? _loadTask;
    private readonly ILogger<EntraOperatorDirectory> _logger;

    public EntraOperatorDirectory(
        TokenCredential tokenCredential,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<EntraOperatorDirectory> logger,
        IOptions<OperatorOptions>? options = null,
        TimeProvider? timeProvider = null)
    {
        _tokenCredential = tokenCredential;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options?.Value ?? new();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<IReadOnlyList<DirectoryPrincipal>>> ListRoleHoldersAsync(
        CancellationToken cancellationToken = default)
    {
        if (_options.GraphCacheDisabled)
            return await LoadLiveWithBudgetAsync(cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (TryGetSnapshot(now, allowStale: false, out var fresh))
            return Result<IReadOnlyList<DirectoryPrincipal>>.Success(fresh);

        Task<Result<IReadOnlyList<DirectoryPrincipal>>> load = GetOrStartLoad();
        var result = await load.WaitAsync(cancellationToken);
        if (result.IsSuccess)
            return result;

        if (TryGetSnapshot(_timeProvider.GetUtcNow(), allowStale: true, out var stale))
        {
            _logger.LogWarning(
                "Microsoft Graph operator directory failed; returning a stale display snapshot: {Error}",
                result.Error);
            return Result<IReadOnlyList<DirectoryPrincipal>>.Success(stale);
        }

        return result;
    }

    private Task<Result<IReadOnlyList<DirectoryPrincipal>>> GetOrStartLoad()
    {
        lock (_loadGate)
        {
            if (_loadTask is null || _loadTask.IsCompleted)
                _loadTask = LoadAndCacheAsync();
            return _loadTask;
        }
    }

    private async Task<Result<IReadOnlyList<DirectoryPrincipal>>> LoadAndCacheAsync()
    {
        var result = await LoadLiveWithBudgetAsync(CancellationToken.None);
        if (result.IsSuccess)
        {
            var entry = new SnapshotCacheEntry(_timeProvider.GetUtcNow(), result.Value);
            lock (_snapshotGate)
                _snapshot = entry;
        }

        return result;
    }

    private async Task<Result<IReadOnlyList<DirectoryPrincipal>>> LoadLiveWithBudgetAsync(
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(DirectoryTimeout);
        return await ListRoleHoldersCoreAsync(budget.Token);
    }

    private bool TryGetSnapshot(
        DateTimeOffset now,
        bool allowStale,
        out IReadOnlyList<DirectoryPrincipal> principals)
    {
        SnapshotCacheEntry? snapshot;
        lock (_snapshotGate)
            snapshot = _snapshot;

        principals = [];
        if (snapshot is null)
            return false;

        TimeSpan age = now - snapshot.FetchedAtUtc;
        int maxAgeSeconds = allowStale
            ? Math.Max(0, _options.GraphCacheStaleOnErrorSeconds)
            : Math.Max(1, _options.GraphCacheTtlSeconds);
        if (allowStale && maxAgeSeconds == 0)
            return false;
        if (age > TimeSpan.FromSeconds(maxAgeSeconds))
            return false;

        principals = snapshot.Principals;
        return true;
    }

    private async Task<Result<IReadOnlyList<DirectoryPrincipal>>> ListRoleHoldersCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Same key the authorization middleware fail-closes on — one setting, Key Vault backed.
            // MUST be a GUID before it reaches an OData query: an unresolved Key Vault reference is a
            // non-empty string ("@Microsoft.KeyVault(SecretUri=…)") that Graph rejects as a syntax
            // error, which reads as a Graph fault rather than the configuration fault it is.
            string? clientId = _configuration["AadClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                return Failure("AadClientId is not configured; cannot query Microsoft Graph.");
            if (!Guid.TryParse(clientId, out _))
                return Failure(
                    "AadClientId is not a GUID; the Key Vault reference is most likely unresolved " +
                    "(check the app setting's configreferences status). Cannot query Microsoft Graph.");
            var tokenResult = await AcquireTokenAsync(cancellationToken);
            if (tokenResult.IsFailure)
                return Failure(tokenResult.Error);

            var client = _httpClientFactory.CreateClient(GraphClientName);
            string encodedClientId = clientId.Replace("'", "''", StringComparison.Ordinal);
            string servicePrincipalEndpoint =
                $"servicePrincipals(appId='{encodedClientId}')?$select=id,appRoles";
            var servicePrincipalResult = await GetAsync<ServicePrincipalResponse>(
                client,
                tokenResult.Value,
                servicePrincipalEndpoint,
                "service principal lookup",
                cancellationToken);
            if (servicePrincipalResult.IsFailure)
                return Failure(servicePrincipalResult.Error);

            string? servicePrincipalId = servicePrincipalResult.Value.Id;
            if (string.IsNullOrWhiteSpace(servicePrincipalId))
                return Failure("Microsoft Graph service principal lookup returned no object id (HTTP 200 OK).");

            IReadOnlyDictionary<string, string> roleMap =
                BuildRoleMap(servicePrincipalResult.Value.AppRoles);
            string assignmentsEndpoint =
                $"servicePrincipals/{Uri.EscapeDataString(servicePrincipalId)}/appRoleAssignedTo?$top=999";
            var assignmentsResult = await GetPagedAsync<AppRoleAssignmentResponse>(
                client,
                tokenResult.Value,
                assignmentsEndpoint,
                "application role assignments",
                cancellationToken);
            if (assignmentsResult.IsFailure)
                return Failure(assignmentsResult.Error);

            var principals = new Dictionary<string, PrincipalAccumulator>(StringComparer.OrdinalIgnoreCase);
            var expandedGroups =
                new Dictionary<string, IReadOnlyList<GroupMemberResponse>>(StringComparer.OrdinalIgnoreCase);
            int groupExpansions = 0;
            foreach (AppRoleAssignmentResponse assignment in assignmentsResult.Value)
            {
                string? principalId = assignment.PrincipalId;
                if (string.IsNullOrWhiteSpace(principalId))
                    return Failure("Microsoft Graph returned an application role assignment without a principal id (HTTP 200 OK).");

                if (string.Equals(assignment.PrincipalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? role = ResolveRole(assignment.AppRoleId, roleMap);
                if (role is null)
                    continue;
                string[] roles = [role];

                if (string.Equals(assignment.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    string groupId = principalId.Trim();
                    string groupName = string.IsNullOrWhiteSpace(assignment.PrincipalDisplayName)
                        ? groupId
                        : assignment.PrincipalDisplayName.Trim();
                    if (!expandedGroups.TryGetValue(groupId, out var members))
                    {
                        groupExpansions++;
                        if (groupExpansions > Math.Max(1, _options.MaxGroupExpansions))
                            return Failure(
                                $"Microsoft Graph group expansion exceeded the configured limit of {_options.MaxGroupExpansions} groups.");

                        string groupEndpoint =
                            $"groups/{Uri.EscapeDataString(groupId)}/transitiveMembers/microsoft.graph.user" +
                            "?$select=id,displayName,userPrincipalName&$top=999";
                        var membersResult = await GetPagedAsync<GroupMemberResponse>(
                            client,
                            tokenResult.Value,
                            groupEndpoint,
                            $"members of group '{groupName}'",
                            cancellationToken,
                            maxItems: Math.Max(1, _options.MaxMembersPerGroup));
                        if (membersResult.IsFailure)
                            return Failure(membersResult.Error);

                        members = membersResult.Value;
                        expandedGroups.Add(groupId, members);
                    }

                    foreach (GroupMemberResponse member in members)
                    {
                        if (string.IsNullOrWhiteSpace(member.Id))
                            continue;

                        AddPrincipal(
                            principals,
                            member.Id,
                            member.DisplayName,
                            member.UserPrincipalName,
                            roles,
                            $"Group:{groupName}");
                    }

                    continue;
                }

                if (!string.Equals(assignment.PrincipalType, "User", StringComparison.OrdinalIgnoreCase))
                    continue;

                string displayName = string.IsNullOrWhiteSpace(assignment.PrincipalDisplayName)
                    ? principalId
                    : assignment.PrincipalDisplayName.Trim();
                string? userPrincipalName = null;
                var userResult = await GetAsync<UserResponse>(
                    client,
                    tokenResult.Value,
                    $"users/{Uri.EscapeDataString(principalId)}?$select=id,displayName,userPrincipalName",
                    $"user lookup for '{displayName}'",
                    cancellationToken);
                if (userResult.IsSuccess)
                {
                    displayName = string.IsNullOrWhiteSpace(userResult.Value.DisplayName)
                        ? displayName
                        : userResult.Value.DisplayName.Trim();
                    userPrincipalName = userResult.Value.UserPrincipalName;
                }
                else
                {
                    // A direct assignment is still useful without the optional profile lookup.
                    // Keep the principal and degrade only the missing UPN/profile fields.
                    _logger.LogWarning(
                        "Microsoft Graph direct user lookup failed for {ObjectId}; continuing without UPN: {Error}",
                        principalId,
                        userResult.Error);
                }

                AddPrincipal(principals, principalId, displayName, userPrincipalName, roles, "Direct");
            }

            IReadOnlyList<DirectoryPrincipal> result = principals.Values
                .Select(principal => principal.ToDirectoryPrincipal())
                .ToArray();
            _logger.LogDebug("Listed {Count} live Entra operator directory principals.", result.Count);
            return Result<IReadOnlyList<DirectoryPrincipal>>.Success(result);
        }
        catch (Exception ex)
        {
            string error =
                $"Microsoft Graph operator directory lookup failed (HTTP status unavailable): {ex.Message}";
            _logger.LogWarning(ex, "Microsoft Graph operator directory lookup failed");
            return Result<IReadOnlyList<DirectoryPrincipal>>.Failure(error);
        }
    }

    /// <summary>
    ///     Bounded on purpose. <c>DefaultAzureCredential</c> walks a chain of sources, and
    ///     where none is present (local runs, E2E) the managed-identity/IMDS probe and the Azure CLI
    ///     subprocess can stall for tens of seconds — long enough to hold the Functions host open on
    ///     an admin page request. Cap it and let the caller report the directory as unavailable.
    /// </summary>
    private async Task<Result<string>> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TokenTimeout);
        try
        {
            var token = await _tokenCredential.GetTokenAsync(
                new TokenRequestContext(GraphScopes),
                timeout.Token);
            return string.IsNullOrWhiteSpace(token.Token)
                ? Result<string>.Failure(
                    "Microsoft Graph authentication returned an empty access token (HTTP status unavailable).")
                : Result<string>.Success(token.Token);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(
                $"Microsoft Graph authentication failed (HTTP status unavailable): {ex.Message}");
        }
    }

    private async Task<Result<T>> GetAsync<T>(
        HttpClient client,
        string accessToken,
        string endpoint,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveUri(endpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result<T>.Failure(BuildHttpFailure(operation, response.StatusCode, body));

            try
            {
                T? value = JsonSerializer.Deserialize<T>(body, JsonOptions);
                return value is null
                    ? Result<T>.Failure(BuildHttpFailure(
                        operation,
                        response.StatusCode,
                        "The response body was empty."))
                    : Result<T>.Success(value);
            }
            catch (JsonException ex)
            {
                return Result<T>.Failure(BuildHttpFailure(
                    operation,
                    response.StatusCode,
                    $"The response was not valid JSON: {ex.Message}"));
            }
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(BuildExceptionFailure(operation, ex));
        }
    }

    private async Task<Result<IReadOnlyList<T>>> GetPagedAsync<T>(
        HttpClient client,
        string accessToken,
        string endpoint,
        string operation,
        CancellationToken cancellationToken,
        int? maxItems = null)
    {
        var items = new List<T>();
        string? nextLink = endpoint;
        var visitedLinks = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            if (!visitedLinks.Add(nextLink))
                return Result<IReadOnlyList<T>>.Failure(
                    $"Microsoft Graph {operation} returned a repeated paging link (HTTP status unavailable).");

            var pageResult = await GetAsync<GraphPage<T>>(
                client,
                accessToken,
                nextLink,
                operation,
                cancellationToken);
            if (pageResult.IsFailure)
                return Result<IReadOnlyList<T>>.Failure(pageResult.Error);

            if (pageResult.Value.Items is not null)
            {
                if (maxItems is { } limit && items.Count + pageResult.Value.Items.Count > limit)
                    return Result<IReadOnlyList<T>>.Failure(
                        $"Microsoft Graph {operation} exceeded the configured limit of {limit} items.");
                items.AddRange(pageResult.Value.Items);
            }
            nextLink = pageResult.Value.NextLink;
        }

        return Result<IReadOnlyList<T>>.Success(items);
    }

    private static Uri ResolveUri(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(new Uri(GraphBaseUrl), endpoint.TrimStart('/'));
    }

    private static IReadOnlyDictionary<string, string> BuildRoleMap(
        IReadOnlyList<AppRoleResponse>? appRoles)
    {
        var roleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (appRoles is null)
            return roleMap;

        foreach (AppRoleResponse appRole in appRoles)
        {
            if (!Guid.TryParse(appRole.Id, out Guid appRoleId))
                continue;

            string? role = appRole.Value?.Trim() switch
            {
                { } value when value.Equals("Operator", StringComparison.OrdinalIgnoreCase) => "Operator",
                { } value when value.Equals("Admin", StringComparison.OrdinalIgnoreCase) => "Admin",
                _ => null
            };
            if (role is not null)
                roleMap[appRoleId.ToString("D")] = role;
        }

        return roleMap;
    }

    private static string? ResolveRole(
        string? appRoleId,
        IReadOnlyDictionary<string, string> roleMap)
    {
        if (string.IsNullOrWhiteSpace(appRoleId)
            || appRoleId.Equals(DefaultAccessRoleId, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(appRoleId, out Guid parsedRoleId))
            return null;

        return roleMap.TryGetValue(parsedRoleId.ToString("D"), out string? role)
            ? role
            : null;
    }

    private static void AddPrincipal(
        IDictionary<string, PrincipalAccumulator> principals,
        string objectId,
        string? displayName,
        string? userPrincipalName,
        IEnumerable<string> roles,
        string source)
    {
        string normalizedObjectId = objectId.Trim();
        if (!principals.TryGetValue(normalizedObjectId, out PrincipalAccumulator? principal))
        {
            principal = new PrincipalAccumulator(
                normalizedObjectId,
                string.IsNullOrWhiteSpace(displayName) ? normalizedObjectId : displayName.Trim(),
                userPrincipalName);
            principals.Add(normalizedObjectId, principal);
        }
        else
        {
            principal.MergeProfile(displayName, userPrincipalName);
        }

        principal.MergeRoles(roles);
        principal.MergeSource(source);
    }

    // Surfaced to an admin in the console, so say what to DO. The raw Graph body is logged, not
    // shown: 404 in particular is not a permission problem at all — it means no service principal
    // exists in this tenant for the configured client id, which no amount of consent will fix.
    private static string BuildHttpFailure(string operation, HttpStatusCode statusCode, string body)
    {
        string status = $"HTTP {(int)statusCode} {statusCode}";
        string cause = statusCode switch
        {
            HttpStatusCode.Forbidden =>
                " The managed identity has not been granted Microsoft Graph directory read by an ARN tenant admin.",
            HttpStatusCode.NotFound =>
                " No service principal exists in this tenant for the configured AadClientId, so there is no directory to read.",
            HttpStatusCode.Unauthorized =>
                " The managed identity could not authenticate to Microsoft Graph.",
            _ => string.Empty
        };
        string details = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : $" Response: {Truncate(body, 500)}";
        return $"Microsoft Graph {operation} failed ({status}).{cause}{details}";
    }

    private static string BuildExceptionFailure(string operation, Exception exception)
    {
        HttpStatusCode? statusCode = exception is HttpRequestException httpException
            ? httpException.StatusCode
            : null;
        string status = statusCode.HasValue
            ? $"HTTP {(int)statusCode.Value} {statusCode.Value}"
            : "HTTP status unavailable";
        string permissionMessage = statusCode == HttpStatusCode.Forbidden
            ? " The managed identity has not been granted Microsoft Graph directory read by an ARN tenant admin."
            : string.Empty;
        return $"Microsoft Graph {operation} failed ({status}).{permissionMessage} {exception.Message}";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private Result<IReadOnlyList<DirectoryPrincipal>> Failure(string error)
    {
        _logger.LogWarning("Microsoft Graph operator directory unavailable: {Error}", error);
        return Result<IReadOnlyList<DirectoryPrincipal>>.Failure(error);
    }

    private sealed record SnapshotCacheEntry(
        DateTimeOffset FetchedAtUtc,
        IReadOnlyList<DirectoryPrincipal> Principals);

    private sealed class PrincipalAccumulator
    {
        private readonly List<string> _roles = [];
        private readonly List<string> _sources = [];

        public PrincipalAccumulator(string objectId, string displayName, string? userPrincipalName)
        {
            ObjectId = objectId;
            DisplayName = displayName;
            UserPrincipalName = userPrincipalName;
        }

        public string ObjectId { get; }
        public string DisplayName { get; private set; }
        public string? UserPrincipalName { get; private set; }

        public void MergeProfile(string? displayName, string? userPrincipalName)
        {
            if (!string.IsNullOrWhiteSpace(displayName)
                && string.Equals(DisplayName, ObjectId, StringComparison.Ordinal))
                DisplayName = displayName.Trim();
            if (string.IsNullOrWhiteSpace(UserPrincipalName)
                && !string.IsNullOrWhiteSpace(userPrincipalName))
                UserPrincipalName = userPrincipalName.Trim();
        }

        public void MergeRoles(IEnumerable<string> roles)
        {
            foreach (string role in roles)
            {
                if ((role.Equals("Operator", StringComparison.Ordinal)
                     || role.Equals("Admin", StringComparison.Ordinal))
                    && !_roles.Contains(role, StringComparer.Ordinal))
                    _roles.Add(role);
            }
        }

        public void MergeSource(string source)
        {
            if (!_sources.Contains(source, StringComparer.Ordinal))
                _sources.Add(source);
        }

        public DirectoryPrincipal ToDirectoryPrincipal() =>
            new(ObjectId, DisplayName, UserPrincipalName, _roles.ToArray(), string.Join(", ", _sources));
    }

    private sealed class GraphPage<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Items { get; init; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
    }

    private sealed class ServicePrincipalResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("appRoles")]
        public List<AppRoleResponse>? AppRoles { get; init; }
    }

    private sealed class AppRoleResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed class AppRoleAssignmentResponse
    {
        [JsonPropertyName("principalId")]
        public string? PrincipalId { get; init; }

        [JsonPropertyName("principalType")]
        public string? PrincipalType { get; init; }

        [JsonPropertyName("principalDisplayName")]
        public string? PrincipalDisplayName { get; init; }

        [JsonPropertyName("appRoleId")]
        public string? AppRoleId { get; init; }
    }

    private sealed class GroupMemberResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; init; }
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; init; }
    }
}
