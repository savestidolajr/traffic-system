using System.Collections.Concurrent;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Auth;

/// <summary>
///     Identity of a person the platform has just seen, as presented by their own token.
///     <paramref name="ObjectId" /> MUST be a real Entra directory object id (the <c>oid</c>
///     claim): it is the row key that joins an assignment to <c>appRoleAssignedTo</c>, so a
///     provider-specific id (the Static Web Apps <c>userId</c>) would create a row no directory
///     state can ever explain.
/// </summary>
/// <param name="LastSignInUtc">
///     Set only by the sign-in path. Null from a read path such as <c>/operators/me</c>, which
///     runs on every page load and must not write a row on each one.
/// </param>
public sealed record OperatorIdentity(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    IReadOnlyList<string> Roles,
    DateTime? LastSignInUtc = null);

/// <summary>
///     Records the people Entra lets in so an admin can scope them to stations.
///
///     The Operators page is a scoping surface, not an access surface — Entra app roles decide
///     sign-in — but an admin cannot scope somebody who has no row. Two paths create one:
///     the Static Web Apps <c>rolesSource</c> call at sign-in (the only place the AAD claims,
///     including <c>oid</c>, reach the backend behind SWA) and <c>/operators/me</c> (Easy Auth
///     direct, where the header carries the claims itself).
///
///     Every failure here is soft: a person who cannot be recorded still signs in and still
///     works unscoped. Refusing them because a table write failed would be a worse outcome
///     than an incomplete admin list.
/// </summary>
public sealed class OperatorRegistrar(
    IOperatorAssignmentRepository repository,
    ILogger<OperatorRegistrar> logger,
    IOptions<OperatorOptions>? options = null,
    IMarketAccessCacheInvalidator? cacheInvalidator = null)
{
    private readonly ConcurrentDictionary<string, RegistrationFlight> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IOperatorAssignmentRepository _repository = repository;
    private readonly ILogger<OperatorRegistrar> _logger = logger;
    private readonly OperatorOptions _options = options?.Value ?? new();

    public async Task<Result<Option<OperatorAssignment>>> RegisterAsync(
        OperatorIdentity identity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.ObjectId))
        {
            _logger.LogDebug("[Operators] Caller has no Entra directory object id; registration skipped");
            return Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None);
        }

        if (_options.RegistrationSingleFlightDisabled)
            return await RegisterCoreAsync(identity, nowUtc, cancellationToken);

        string objectId = identity.ObjectId.Trim();
        while (true)
        {
            if (_inFlight.TryGetValue(objectId, out var existingFlight))
            {
                if (existingFlight.TryMerge(identity, nowUtc))
                    return await existingFlight.Completion.Task.WaitAsync(cancellationToken);

                await existingFlight.Completion.Task.WaitAsync(cancellationToken);
                _inFlight.TryRemove(
                    new KeyValuePair<string, RegistrationFlight>(objectId, existingFlight));
                continue;
            }

            var flight = new RegistrationFlight(this, objectId, identity, nowUtc);
            if (_inFlight.TryAdd(objectId, flight))
            {
                _ = flight.RunAsync();
                return await flight.Completion.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private Task<Result<Option<OperatorAssignment>>> RegisterCoreAsync(
        OperatorIdentity identity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        (OperatorIdentity Identity, DateTime NowUtc) Snapshot() => (identity, nowUtc);
        return RegisterCoreAsync(Snapshot, Snapshot, cancellationToken);
    }

    private async Task<Result<Option<OperatorAssignment>>> RegisterCoreAsync(
        Func<(OperatorIdentity Identity, DateTime NowUtc)> snapshot,
        Func<(OperatorIdentity Identity, DateTime NowUtc)> finalSnapshot,
        CancellationToken cancellationToken)
    {
        var initial = snapshot();
        OperatorIdentity identity = initial.Identity;
        DateTime nowUtc = initial.NowUtc;

        try
        {
            var existingResult = await _repository.GetAsync(identity.ObjectId, cancellationToken);
            if (existingResult.IsFailure)
            {
                _logger.LogWarning(
                    "[Operators] Registration lookup failed for {ObjectId}; continuing without a row: {Error}",
                    identity.ObjectId, existingResult.Error);
                return Result<Option<OperatorAssignment>>.Failure(existingResult.Error);
            }

            // A coalesced caller may have merged a newer name, UPN, role set, or sign-in timestamp
            // while the assignment lookup was in flight. Take one final snapshot before deciding
            // what to persist so those identity changes are not lost.
            var latest = finalSnapshot();
            identity = latest.Identity;
            nowUtc = latest.NowUtc;
            string displayName = identity.DisplayName.Trim();

            if (existingResult.Value.IsNone)
            {
                var created = NewAssignment(identity, displayName, nowUtc);
                var createResult = await _repository.UpsertAsync(created, cancellationToken);
                if (createResult.IsFailure)
                {
                    _logger.LogWarning(
                        "[Operators] Registration failed for {ObjectId}; continuing without a row: {Error}",
                        identity.ObjectId, createResult.Error);
                    return Result<Option<OperatorAssignment>>.Failure(createResult.Error);
                }
                cacheInvalidator?.InvalidateScope(created.ObjectId);

                _logger.LogInformation(
                    "[Operators] Registered or refreshed operator {ObjectId} as {DisplayName} with role(s) [{Roles}]",
                    created.ObjectId, created.DisplayName, string.Join(", ", identity.Roles));
                return Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(created));
            }

            var existing = existingResult.Value.Value;
            var assignmentToSave = Refreshed(existing, identity, displayName, nowUtc, _options);
            if (assignmentToSave is null)
                return Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(existing));

            var saveResult = await _repository.UpsertAsync(assignmentToSave, cancellationToken);
            if (saveResult.IsFailure)
            {
                // The assignment read succeeded. Keep using that known administrator-controlled
                // scope even when a best-effort identity refresh cannot be written.
                _logger.LogWarning(
                    "[Operators] Registration refresh failed for {ObjectId}; retaining the existing assignment: {Error}",
                    identity.ObjectId, saveResult.Error);
                return Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(existing));
            }
            cacheInvalidator?.InvalidateScope(assignmentToSave.ObjectId);

            _logger.LogInformation(
                "[Operators] Registered or refreshed operator {ObjectId} as {DisplayName} with role(s) [{Roles}]",
                assignmentToSave.ObjectId, assignmentToSave.DisplayName, string.Join(", ", identity.Roles));
            return Result<Option<OperatorAssignment>>.Success(
                Option<OperatorAssignment>.Some(assignmentToSave));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Operators] Registration failed for {ObjectId}; continuing without a row",
                identity.ObjectId);
            return Result<Option<OperatorAssignment>>.Failure(ex.Message);
        }
    }

    private static OperatorAssignment NewAssignment(
        OperatorIdentity identity, string displayName, DateTime nowUtc)
    {
        // Empty markets means unscoped, never denied — a person Entra admitted must be able to
        // work before an admin has got round to scoping them.
        return new OperatorAssignment(
            identity.ObjectId,
            displayName,
            [],
            nowUtc,
            displayName.Length > 0 ? displayName : "unknown",
            identity.UserPrincipalName,
            identity.Roles,
            identity.LastSignInUtc);
    }

    /// <summary>
    ///     Returns the row to write, or null when nothing observable changed. Markets are the
    ///     admin's to set and are always carried across untouched.
    /// </summary>
    private static OperatorAssignment? Refreshed(
        OperatorAssignment existing,
        OperatorIdentity identity,
        string displayName,
        DateTime nowUtc,
        OperatorOptions options)
    {
        bool nameChanged = displayName.Length > 0 &&
                           !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal);
        bool upnChanged = !string.IsNullOrWhiteSpace(identity.UserPrincipalName) &&
                          !string.Equals(existing.UserPrincipalName, identity.UserPrincipalName,
                              StringComparison.OrdinalIgnoreCase);
        // An empty incoming role list is "this path did not see any", never "they lost their
        // roles" — the directory read is what proves a removal, so never erase on a blank.
        bool rolesChanged = identity.Roles.Count > 0 &&
                            (existing.RolesAtSignIn is null ||
                             !RolesMatch(existing.RolesAtSignIn, identity.Roles));

        bool signInTimestampDue = identity.LastSignInUtc is { } signInUtc &&
            (existing.LastSignInUtc is not { } previousSignIn ||
             signInUtc > previousSignIn &&
             signInUtc - previousSignIn >=
             TimeSpan.FromSeconds(Math.Max(0, options.SignInTimestampRefreshSeconds)));

        // A read path such as /operators/me has no sign-in timestamp and therefore never writes an
        // unchanged row. Timestamp-only sign-in writes are also bounded so coordinated token
        // refreshes do not turn the rolesSource bridge into a table write storm.
        if (!nameChanged && !upnChanged && !rolesChanged && !signInTimestampDue)
            return null;

        DateTime? lastSignInUtc = existing.LastSignInUtc;
        if (identity.LastSignInUtc is { } incomingSignIn &&
            (lastSignInUtc is null || incomingSignIn > lastSignInUtc.Value))
            lastSignInUtc = incomingSignIn;

        // UpdatedAtUtc is also a freshness marker for the assignment row. A skewed host clock
        // must not make a changed identity look older than the row it replaces.
        DateTime updatedAtUtc = nowUtc > existing.UpdatedAtUtc
            ? nowUtc
            : existing.UpdatedAtUtc;

        return new OperatorAssignment(
            existing.ObjectId,
            nameChanged ? displayName : existing.DisplayName,
            existing.Markets,
            updatedAtUtc,
            nameChanged ? displayName : existing.UpdatedBy,
            identity.UserPrincipalName ?? existing.UserPrincipalName,
            identity.Roles.Count > 0 ? identity.Roles : existing.RolesAtSignIn,
            lastSignInUtc);
    }

    private static bool RolesMatch(
        IReadOnlyList<string> existingRoles,
        IReadOnlyList<string> incomingRoles)
    {
        if (existingRoles.Count != incomingRoles.Count)
            return false;

        foreach (string role in existingRoles)
        {
            if (!incomingRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private sealed class RegistrationFlight(
        OperatorRegistrar owner,
        string objectId,
        OperatorIdentity identity,
        DateTime nowUtc)
    {
        private readonly object _gate = new();
        private OperatorIdentity _identity = identity;
        private DateTime _nowUtc = nowUtc;
        private bool _accepting = true;

        public TaskCompletionSource<Result<Option<OperatorAssignment>>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryMerge(OperatorIdentity identity, DateTime nowUtc)
        {
            lock (_gate)
            {
                if (!_accepting)
                    return false;

                _identity = MergeIdentity(_identity, identity);
                if (nowUtc > _nowUtc)
                    _nowUtc = nowUtc;
                return true;
            }
        }

        private (OperatorIdentity Identity, DateTime NowUtc) Snapshot()
        {
            lock (_gate)
                return (_identity, _nowUtc);
        }

        private (OperatorIdentity Identity, DateTime NowUtc) SealAndSnapshot()
        {
            lock (_gate)
            {
                _accepting = false;
                return (_identity, _nowUtc);
            }
        }

        public async Task RunAsync()
        {
            try
            {
                if (!owner._options.RegistrationDebounceDisabled &&
                    owner._options.RegistrationDebounceMilliseconds > 0)
                {
                    await Task.Delay(owner._options.RegistrationDebounceMilliseconds);
                }

                var result = await owner.RegisterCoreAsync(
                    Snapshot, SealAndSnapshot, CancellationToken.None);
                Completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                owner._logger.LogWarning(ex,
                    "[Operators] Coalesced registration failed for {ObjectId}; continuing without a row",
                    objectId);
                Completion.TrySetResult(Result<Option<OperatorAssignment>>.Failure(ex.Message));
            }
            finally
            {
                owner._inFlight.TryRemove(objectId, out _);
            }
        }
    }

    private static OperatorIdentity MergeIdentity(
        OperatorIdentity current,
        OperatorIdentity incoming)
    {
        DateTime? lastSignIn = current.LastSignInUtc;
        if (incoming.LastSignInUtc is { } incomingSignIn &&
            (lastSignIn is null || incomingSignIn > lastSignIn.Value))
            lastSignIn = incomingSignIn;

        return new OperatorIdentity(
            current.ObjectId,
            string.IsNullOrWhiteSpace(incoming.DisplayName) ? current.DisplayName : incoming.DisplayName,
            string.IsNullOrWhiteSpace(incoming.UserPrincipalName)
                ? current.UserPrincipalName
                : incoming.UserPrincipalName,
            incoming.Roles.Count > 0 ? incoming.Roles : current.Roles,
            lastSignIn);
    }
}
