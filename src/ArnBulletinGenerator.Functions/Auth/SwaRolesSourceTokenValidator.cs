using System.Security.Claims;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ArnBulletinGenerator.Functions.Auth;

/// <summary>A sign-in whose access token passed full cryptographic validation.</summary>
public sealed record ValidatedSignIn(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    IReadOnlyList<string> Roles);

/// <summary>
///     Cryptographically validates the access token Static Web Apps sends with the
///     <c>rolesSource</c> sign-in POST (<c>POST /api/GetRoles</c>).
///
///     Behind the SWA linked backend the <c>X-MS-CLIENT-PRINCIPAL</c> header carries only
///     <c>identityProvider</c>, <c>userId</c>, <c>userDetails</c> and <c>userRoles</c> - never a
///     <c>claims</c> array - so the caller's Entra <c>oid</c> never reaches the API that way.
///     The rolesSource POST is the only moment SWA sends BOTH the real <c>oid</c> (inside the
///     token's claims) and the per-app <c>userId</c>, which is what makes correlation possible.
///
///     This validator is the gate for that path: NOTHING is persisted from the raw payload. The
///     <c>oid</c> and roles written to storage must come from a token that passed full
///     cryptographic validation (signature, issuer, audience, lifetime), because the endpoint is
///     externally reachable - an unauthenticated POST to the Function App hostname bypasses the
///     SWA route, so an attacker can invent a payload. See the alias/registration consumers for
///     the persistence rules (first-writer-wins aliases, fail-soft sign-in).
///
///     Thread safety: registered as a singleton. The OIDC metadata manager is created exactly
///     once via a lazy initializer (<see cref="LazyThreadSafetyMode.ExecutionAndPublication" />);
///     it caches and auto-refreshes the signing keys internally and is safe for concurrent use.
///     <see cref="JsonWebTokenHandler" /> is stateless and thread-safe, so one shared instance is
///     used. Fail-closed: any absent/malformed/unsigned/expired/mis-audienced/mis-issued token,
///     any configuration gap, or any unexpected error yields <see cref="Option{T}.None" />, never
///     a throw (except cancellation).
/// </summary>
public class SwaRolesSourceTokenValidator
{
    private const string OidClaim = "oid";
    private const string OidClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string RoleClaim = "roles";
    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string NameClaim = "name";

    private static readonly string[] UpnClaimTypes = ["preferred_username", "upn", "email"];

    private readonly ILogger<SwaRolesSourceTokenValidator> _logger;
    private readonly string? _clientId;
    private readonly string? _tenantId;
    private readonly Lazy<IConfigurationManager<OpenIdConnectConfiguration>>? _configurationManager;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    ///     Reads the app settings once, at construction. The validator is registered as a
    ///     singleton, so this runs exactly once per process - the "log once" guard is inherent:
    ///     a blank setting is reported in the startup logs, never spammed on every sign-in.
    /// </summary>
    public SwaRolesSourceTokenValidator(
        IConfiguration configuration,
        ILogger<SwaRolesSourceTokenValidator> logger)
        : this(configuration, logger, configurationManager: null)
    {
    }

    /// <summary>
    ///     Test seam: swaps the OIDC metadata source for a caller-supplied
    ///     <see cref="IConfigurationManager{T}" /> so validation can be exercised offline.
    ///     <c>null</c> builds the real <see cref="ConfigurationManager{T}" /> against the tenant's
    ///     well-known endpoint. Disabled (blank settings) always wins over an injected manager.
    /// </summary>
    internal SwaRolesSourceTokenValidator(
        IConfiguration configuration,
        ILogger<SwaRolesSourceTokenValidator> logger,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager)
    {
        _logger = logger;
        _clientId = configuration["AadClientId"];
        _tenantId = configuration["AadTenantId"];

        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_tenantId))
        {
            // Inert, never fatal: sign-in must not depend on token validation. Logged exactly
            // once because the singleton is constructed once per process.
            logger.LogWarning(
                "[SwaRoles] Token validator is inert: AadClientId is {ClientIdState} and AadTenantId is {TenantIdState}; rolesSource access tokens will not be validated",
                string.IsNullOrWhiteSpace(_clientId) ? "blank" : "set",
                string.IsNullOrWhiteSpace(_tenantId) ? "blank" : "set");
            _configurationManager = null;
            return;
        }

        _configurationManager = configurationManager is null
            ? new Lazy<IConfigurationManager<OpenIdConnectConfiguration>>(
                BuildConfigurationManager, LazyThreadSafetyMode.ExecutionAndPublication)
            : new Lazy<IConfigurationManager<OpenIdConnectConfiguration>>(
                () => configurationManager, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    ///     None when the token is absent, malformed, unsigned, expired, issued for another
    ///     audience/issuer, signed by an untrusted key, or lacking an <c>oid</c> claim; also None
    ///     when the validator is not configured. Cancellation always propagates.
    /// </summary>
    public virtual async Task<Option<ValidatedSignIn>> ValidateAsync(
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        // Absent/whitespace is the normal case for a provider that sends no access token - not
        // a failure worth logging.
        if (string.IsNullOrWhiteSpace(accessToken))
            return Option<ValidatedSignIn>.None;

        if (_configurationManager is null)
            return Option<ValidatedSignIn>.None;

        string clientId = _clientId!;
        string tenantId = _tenantId!;

        try
        {
            var openIdConfiguration = await _configurationManager.Value
                .GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                IssuerSigningKeys = openIdConfiguration.SigningKeys,
                ValidAudiences = [clientId, $"api://{clientId}"],
                ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/"
                ],
            };

            // JsonWebTokenHandler.ValidateTokenAsync takes no CancellationToken: the call is pure
            // in-memory crypto once GetConfigurationAsync above has the signing keys, so there is
            // nothing left to cancel.
            var result = await _handler
                .ValidateTokenAsync(accessToken, validationParameters)
                .ConfigureAwait(false);

            if (!result.IsValid)
            {
                LogRejectionDiagnostics(accessToken, result.Exception);
                return Option<ValidatedSignIn>.None;
            }

            List<Claim> claims = result.ClaimsIdentity?.Claims.ToList() ?? [];

            string? objectId = FirstPresent(claims, OidClaim, OidClaimUri);
            if (objectId is null)
            {
                // A cryptographically valid token without a directory key is unusable: the oid
                // is the row key that joins an assignment to appRoleAssignedTo, so registering
                // under anything else would create a row no directory state can explain.
                _logger.LogWarning(
                    "[SwaRoles] Access token passed validation but carries no oid claim; sign-in cannot be correlated to a directory object");
                return Option<ValidatedSignIn>.None;
            }

            var roles = claims
                .Where(IsRoleClaim)
                .Select(static claim => claim.Value)
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? userPrincipalName = FirstPresent(claims, UpnClaimTypes);
            string displayName = FirstPresent(claims, NameClaim) ?? string.Empty;

            return Option<ValidatedSignIn>.Some(
                new ValidatedSignIn(objectId, displayName, userPrincipalName, roles));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRejectionDiagnostics(accessToken, ex);
            return Option<ValidatedSignIn>.None;
        }
    }

    private IConfigurationManager<OpenIdConnectConfiguration> BuildConfigurationManager()
        => new ConfigurationManager<OpenIdConnectConfiguration>(
            $"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

    /// <summary>
    ///     Logs the token's OBSERVED <c>aud</c> and <c>iss</c>, parsed WITHOUT validation and purely
    ///     for diagnostics, plus the failure reason. This exists so live traffic tells us whether
    ///     the rolesSource access token's audience is already our client id - Microsoft does not
    ///     document the default - before anyone changes <c>login.loginParameters</c> on a live
    ///     Static Web App. The token itself is never logged, and neither is any claim value other
    ///     than aud/iss. IdentityModel also redacts token material from exception messages by
    ///     default (<c>IdentityModelEventSource.ShowPII</c> is false).
    /// </summary>
    private void LogRejectionDiagnostics(string accessToken, Exception exception)
    {
        string? audience = null;
        string? issuer = null;
        try
        {
            var token = new JsonWebToken(accessToken);
            var audiences = token.Audiences.ToList();
            if (audiences.Count > 0)
                audience = string.Join(", ", audiences);
            issuer = token.Issuer;
        }
        catch
        {
            // Malformed or unparseable token: there is nothing to report.
        }

        _logger.LogWarning(
            "[SwaRoles] Access token failed validation: {Failure}: {Reason}; observed aud=[{Aud}] iss=[{Iss}] (aud/iss logged so live traffic can confirm the rolesSource audience before any login.loginParameters change)",
            exception.GetType().Name,
            exception.Message,
            audience ?? "<unparseable>",
            issuer ?? "<unparseable>");
    }

    private static bool IsRoleClaim(Claim claim)
        => string.Equals(claim.Type, RoleClaim, StringComparison.OrdinalIgnoreCase)
           || string.Equals(claim.Type, RoleClaimUri, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the value of the first claim whose type matches, scanning types in preference order.</summary>
    private static string? FirstPresent(IEnumerable<Claim> claims, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            foreach (Claim claim in claims)
            {
                if (string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(claim.Value))
                    return claim.Value;
            }
        }

        return null;
    }
}
