using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Functions.Auth;

namespace ArnBulletinGenerator.Functions.Middleware;

/// <summary>
///     Defense-in-depth auth + role-based access control. The Function App's Easy Auth
///     (Bicep authsettingsV2) is the primary boundary; this middleware re-validates the
///     X-MS-CLIENT-PRINCIPAL identity and enforces a route → required-role matrix.
///
///     The principal is parsed by <see cref="ClientPrincipal"/>, which accepts both the
///     App Service Easy Auth blob (direct bearer calls / local-dev mock) and the Static
///     Web Apps client principal forwarded by the SWA linked backend.
///
///     Roles (AAD app roles on the operator app):
///         - Operator: everything market- or job-scoped — read schedules/jobs, approve,
///                     reject, regenerate, publish, edit an assigned station and trigger
///                     an ad-hoc bulletin for one — plus curating the shared speech
///                     libraries (voices, pronunciation, delivery styles, bookends).
///         - Admin:    everything Operator can do, plus surfaces with no market to scope against —
///                     numeric editorial settings, banned words, global guidance and promotion, the
///                     operator assignment register, bulk config transfer — and every DELETE.
///
///     Route matrix lives in <see cref="RouteRoleMatrix"/>.
/// </summary>
public sealed class AuthorizationMiddleware(
    IConfiguration configuration,
    ILogger<AuthorizationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";
    private const string HealthPath = "/health";

    public const string RoleOperator = "Operator";
    public const string RoleAdmin = "Admin";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData is null)
        {
            // Non-HTTP trigger (queue/timer). Platform-internal — pass through.
            await next(context);
            return;
        }

        string path = httpRequestData.Url.AbsolutePath;
        if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/GetRoles", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Fail closed if Easy Auth is not configured at the platform level.
        // If AadClientId is empty, the Bicep skips authsettingsV2, which means
        // Easy Auth is OFF and any X-MS-CLIENT-PRINCIPAL header is attacker-controlled.
        string? aadClientId = configuration["AadClientId"];
        if (string.IsNullOrWhiteSpace(aadClientId))
        {
            logger.LogCritical(
                "AAD client id is not configured. Function endpoints will refuse all requests. " +
                "Configure AadClientId via Key Vault and enable Easy Auth on the Function App.");
            await WriteJsonAsync(context, httpRequestData, HttpStatusCode.ServiceUnavailable,
                "{\"error\":\"Authentication is not configured\"}");
            return;
        }

        // DEV: when devAuthFallbackRole is active, accept a missing principal header and
        // synthesize a dev identity so unauthenticated callers can use the API. Read the setting
        // from this request's configuration rather than process-global environment state: the
        // latter made concurrent test/request decisions depend on whichever invocation ran last.
        string? devFallbackRole = ResolveDevFallbackRole();
        ClientPrincipal? principal = null;
        bool hasPrincipalHeader = httpRequestData.Headers.TryGetValues(PrincipalHeader, out var principalHeaders)
            && principalHeaders.Any(value => !string.IsNullOrWhiteSpace(value));

        if (!ClientPrincipal.TryReadHeaderJson(httpRequestData, out string? principalJson))
        {
            // A malformed non-empty header is not the same as a missing header. Never let the
            // development fallback turn an invalid credential into an authenticated request.
            if (hasPrincipalHeader)
            {
                logger.LogWarning("Request rejected: malformed or invalid {Header} header. {Method} {Path}",
                    PrincipalHeader, httpRequestData.Method, path);
                await WriteJsonAsync(context, httpRequestData, HttpStatusCode.Unauthorized,
                    "{\"error\":\"Authentication required\"}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(devFallbackRole))
            {
                logger.LogDebug(
                    "DEV AUTH BYPASS: no {Header} header — synthesizing dev principal with role '{Role}'. {Method} {Path}",
                    PrincipalHeader, devFallbackRole, httpRequestData.Method, path);
                principal = new ClientPrincipal(
                    ObjectId: "dev-user",
                    Name: "Dev User",
                    Roles: new[] { devFallbackRole });
            }
            else
            {
                logger.LogWarning("Request rejected: missing or empty {Header} header. {Method} {Path}",
                    PrincipalHeader, httpRequestData.Method, path);
                await WriteJsonAsync(context, httpRequestData, HttpStatusCode.Unauthorized,
                    "{\"error\":\"Authentication required\"}");
                return;
            }
        }
        else
        {
            WarnOnceIfBypassActive(devFallbackRole);

            if (!ClientPrincipal.TryFromJson(principalJson!, devFallbackRole, out principal, out string? rejectionReason))
            {
                logger.LogWarning("Request rejected: {Reason}. {Method} {Path}",
                    rejectionReason, httpRequestData.Method, path);
                await WriteJsonAsync(context, httpRequestData, HttpStatusCode.Unauthorized,
                    "{\"error\":\"Authentication required\"}");
                return;
            }
        }

        string requiredRole = RouteRoleMatrix.RequiredRole(httpRequestData.Method, path);
        if (!HasRequiredRole(principal!.Roles, requiredRole))
        {
            logger.LogWarning(
                "Request forbidden: caller roles [{Roles}] lack required role '{Required}'. {Method} {Path}",
                string.Join(",", principal.Roles), requiredRole, httpRequestData.Method, path);
            await WriteJsonAsync(context, httpRequestData, HttpStatusCode.Forbidden,
                $"{{\"error\":\"Forbidden — '{requiredRole}' role required\"}}");
            return;
        }

        await next(context);
    }

    private int _bypassWarned;

    private string? ResolveDevFallbackRole()
    {
        // App settings arrive through IConfiguration in the host. Support both the literal
        // environment-variable spelling used by the deployment and the normalized section key
        // used by configuration providers.
        string? configuredRole = configuration[ClientPrincipal.DevFallbackRoleSetting]
            ?? configuration["Auth:DevFallbackRole"];
        return string.IsNullOrWhiteSpace(configuredRole) ? null : configuredRole.Trim();
    }

    private void WarnOnceIfBypassActive(string? devFallbackRole)
    {
        if (!string.IsNullOrWhiteSpace(devFallbackRole) && Interlocked.Exchange(ref _bypassWarned, 1) == 0)
            logger.LogWarning(
                "DEV AUTH BYPASS active: every authenticated caller is granted the '{Role}' role via {Setting}. " +
                "This MUST be empty in test/prod.", devFallbackRole, ClientPrincipal.DevFallbackRoleSetting);
    }

    internal static bool TryParsePrincipalForTesting(string principalJson, out HashSet<string> roles, out string? rejectionReason)
    {
        bool ok = ClientPrincipal.TryFromJson(principalJson, devFallbackRole: null, out var principal, out rejectionReason);
        roles = new HashSet<string>(principal?.Roles ?? [], StringComparer.OrdinalIgnoreCase);
        return ok;
    }

    internal static bool HasRequiredRoleForTesting(IEnumerable<string> callerRoles, string requiredRole)
        => HasRequiredRole(callerRoles, requiredRole);

    private static bool HasRequiredRole(IEnumerable<string> callerRoles, string requiredRole)
    {
        if (callerRoles.Any(r => string.Equals(r, RoleAdmin, StringComparison.OrdinalIgnoreCase)))
            return true;
        return string.Equals(requiredRole, RoleOperator, StringComparison.OrdinalIgnoreCase)
            && callerRoles.Any(r => string.Equals(r, RoleOperator, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonAsync(
        FunctionContext context,
        HttpRequestData request,
        HttpStatusCode status,
        string body)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(body);
        context.GetInvocationResult().Value = response;
    }
}

/// <summary>
///     Maps (HTTP method, URL path) → required role. Conservative default: Admin.
/// </summary>
internal static class RouteRoleMatrix
{
    public static string RequiredRole(string method, string absolutePath)
    {
        // Strip query string & normalize.
        string path = absolutePath.ToLowerInvariant();
        string m = method.ToUpperInvariant();

        // Content-safety terms (banned words + exceptions) are a compliance control — Admin only, all methods.
        if (path.Contains("/safety/terms", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleAdmin;

        // Config transfer (bundle export/import + pending approvals) reads AND writes the whole
        // editable configuration — Admin only, all methods.
        if (path.StartsWith("/api/config", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleAdmin;

        // Activity/change audit — readable by operators. It is the accountability half of the
        // library grants below: an operator who may re-tune a shared voice or dictionary should
        // be able to see who changed what, and hiding the record from the people doing the work
        // buys nothing (every entry is already attributed, and nothing here is mutable).
        // Read-only by construction — there is no non-GET /api/activity route.
        if (path.StartsWith("/api/activity", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // The HERE map key endpoint serves the browser-safe JS key to the console. The GET
        // default already resolves it to Operator, but pin it explicitly: it is the one
        // endpoint whose response would be valuable to anonymous internet callers, so a future
        // GET-default change must not silently open it.
        if (path.StartsWith("/api/map-config", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // Read-only endpoints — Operator OR Admin, except the assignment register
        // (operator → market assignments are admin-managed; /operators/me stays open).
        if (m == "GET")
            return path.Contains("/operators", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/me")
                ? AuthorizationMiddleware.RoleAdmin
                : AuthorizationMiddleware.RoleOperator;

        // Operators maintain per-market priority roads (WS2: experts seed, operators refine).
        if (m == "PUT" && path.EndsWith("/priority-roads"))
            return AuthorizationMiddleware.RoleOperator;

        // Operators own verified pronunciations: they are the ones listening to the audio and
        // deciding a flagged word aired fine, so gating removal behind Admin would strand them
        // with a mistake they cannot undo. The activity log is the accountability mechanism.
        if (m == "PUT" && path.EndsWith("/verified-pronunciations"))
            return AuthorizationMiddleware.RoleOperator;

        // Filler statements are per-market operational copy in the same whole-list PUT shape as
        // priority roads and verified pronunciations, and the operator is who hears the filler
        // read badly on air — so it belongs with them, not with network-wide editorial policy.
        if (m == "PUT" && path.EndsWith("/filler-statements"))
            return AuthorizationMiddleware.RoleOperator;

        // Per-market incident-class score boosts are the fourth control on the same Guidance
        // page as priority roads, filler statements and verified pronunciations, and they arrive
        // in the identical whole-list PUT shape. The operator is who hears a crash buried behind
        // roadworks and needs to re-weight it, so it belongs with them for the same reason the
        // other three do. IncidentPrioritiesHttpTrigger runs EnsureMarketAccessAsync before the
        // write, so an operator can only tune a market they are assigned to. It is recoverable
        // and attributable the way the grants above require: each rule resets to its default
        // boost, the bonus is clamped to -100..100 so no single rule can dominate ranking, and
        // ActivityLoggingMiddleware records the mutation. Without this branch the route fell to
        // the Admin default below while the console offered the sliders to operators, so every
        // commit 403'd.
        if (m == "PUT" && path.EndsWith("/incident-priorities"))
            return AuthorizationMiddleware.RoleOperator;

        // Operators maintain per-market/station standing guidance; global guidance + promotion are Admin.
        // The unscoped PUT /guidance and DELETE /guidance/{id} are scope-dependent: the scope lives in
        // the payload/query, not the route, so GuidanceHttpTrigger re-checks Admin for Global rows and
        // applies the same market guard as /markets/{m}/guidance to Market/Station rows (Station →
        // market resolved through the schedule catalogue). Market-scoped routes match the same patterns
        // below and keep their existing in-route guard.
        if (m == "PUT" && path.EndsWith("/guidance"))
            return AuthorizationMiddleware.RoleOperator;
        if (m == "DELETE" && path.Contains("/guidance/", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // Operator workflow: approve / reject / regenerate / publish a job, choose what airs in a
        // slot (air-source override — repeat an earlier bulletin or the filler cart), correct a
        // pronunciation on the dictionary it aired with, and iterate on the words and the read
        // (rewrite / retone / audition / direct). Publish is the last step of the same review
        // decision, not a separate privilege — an operator who may put a bulletin on air must be
        // able to get it there, or the on-call model needs an Admin awake too.
        // Rewrite/retone/audition/direct are the script-steering loop the operator workstation is
        // built around and carry the same per-job market guard as approve/reject, so they belong at
        // the same trust level. Rewrite is the operator's own "Ask for a change" control in the
        // script editor (ScriptAssist, Content mode) and mutates NOTHING: it returns a preview the
        // operator reads as a diff, and the words only reach air through the existing verbatim
        // re-voice path (/regenerate), where the safety reviewer sees them again. The controls that
        // make these safe are elsewhere and unchanged: the job's market guard
        // (JobLifecycleHttpTrigger / ScriptRetoneHttpTrigger / ScriptAuditionHttpTrigger /
        // ScriptDirectionHttpTrigger / ScriptRewriteHttpTrigger), the grounding + content-safety
        // hard blocks on the rewrite itself, the ETag revision precondition, and the status checks
        // inside JobService. The pronunciation route is job-addressed for the same reason — it
        // inherits that market guard, where the library-wide PUT
        // /pronunciation-dictionaries/{id} has no market to scope against and stays Admin.
        if (m == "POST" && (
                path.Contains("/jobs/", StringComparison.OrdinalIgnoreCase) &&
                (path.EndsWith("/approve") || path.EndsWith("/reject") || path.EndsWith("/reject-to-fallback")
                 || path.EndsWith("/regenerate") || path.EndsWith("/publish") || path.EndsWith("/air-source")
                 || path.EndsWith("/pronunciation-rules") || path.EndsWith("/retone")
                 || path.EndsWith("/rewrite")
                 || path.EndsWith("/audition") || path.EndsWith("/direct"))))
            return AuthorizationMiddleware.RoleOperator;

        // Event Ledger verdicts are the operator's standing judgment on a road event (the Desk's
        // confirm/flag/kill), market-guarded inside LedgerHttpTrigger the same way approve/reject
        // are; spot-check sign-off is the after-the-fact review of a bulletin that aired without
        // an operator, which is operator review work by definition.
        if (m == "POST" && path.Contains("/ledger/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith("/verdict"))
            return AuthorizationMiddleware.RoleOperator;
        if (m == "POST" && path.Contains("/spot-checks/", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // A station's own schedule is per-market operational config — cron, voice, dictionary,
        // delivery style, duration, coverage, content toggles, and the enable switch — and the
        // market's operator is who owns the read that airs from it. Same reasoning (and the same
        // whole-object PUT shape) as priority roads / filler statements / market guidance, and the
        // route is already market-scoped by ScheduleHttpTrigger's EnsureMarketAccessAsync, so an
        // operator can only edit a station they are assigned to — including "create", which is the
        // same PUT and 403s for any market outside their assignment. DELETE stays Admin: removing a
        // station is not an operational act, and the operator has no way to undo it.
        if (m == "PUT" && path.StartsWith("/api/schedules/", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // Recording a voiceover — an ad, a promo, a station ident — on a library voice is
        // production work an operator does, so it sits with them. It is the one Operator write
        // that is deliberately NOT market-scoped, because a voice belongs to the library and an
        // ad read belongs to no station's slot. What makes the scoping rule unnecessary here is
        // that nothing is mutated: no job, no schedule, no shared config. It renders one throwaway
        // WAV, bounded by the per-request script ceiling, and hands it back. The paired
        // GET voices/voiceovers/{file} is Operator by the GET default and will only serve files
        // this route wrote (VoiceoverHttpTrigger pins the name pattern), so it cannot be turned
        // into a reader for bulletin audio whose market guard lives on the job routes.
        if (m == "POST" && path.EndsWith("/voiceover", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // The bookend library is the station's own opening and closing lines — the words the
        // operator hears at the top and tail of every read they approve — so authoring belongs
        // with them, not with an admin they have to wait for. This is the second deliberate
        // exemption from the market-scoping rule and, unlike the voiceover render, it DOES mutate
        // shared config: one library serves every station, so an operator editing a pair can
        // change what another market airs. Accepted with eyes open, because the alternatives are
        // worse: scoping a write to "only pairs assigned solely to my markets" would leave an
        // unassigned or shared pair editable by nobody but an admin, which is the current defect
        // restated. The controls that make it safe are elsewhere: every write mints an immutable
        // version with an author and a change note (BookendService), the activity log records the
        // mutation (ActivityLoggingMiddleware), any version is restorable, and a pair in use
        // cannot be deleted (409 library.in_use).
        //
        // DELETE stays Admin, matching DELETE /schedules/{marketId}: archive/unarchive is the
        // reversible retire-in-place path an operator needs, where a delete is unrecoverable and
        // strands every station pointing at the pair.
        if (m != "DELETE" && path.StartsWith("/api/bookends", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // The shared LIBRARIES — voice profiles, pronunciation dictionaries, delivery styles —
        // extend the bookend grant above to the rest of the shelf (2026-08-13, second wave). The
        // reasoning is the same and so are the controls: every write mints an immutable version
        // carrying its author and change note, ActivityLoggingMiddleware records the mutation, any
        // version is restorable, Archive/Unarchive is the reversible retire-in-place path, and
        // LibraryReferenceGuard refuses to delete anything a station still points at
        // (409 library.in_use). Those are generic, not bookend-specific.
        //
        // What this deliberately abandons is the idea that scopability is the boundary. It was
        // only ever a proxy: POST /jobs/{id}/pronunciation-rules already let an operator mint a
        // new dictionary version, so the SAME mutation was Operator via a job and Admin via the
        // library — a difference of URL shape, not of consequence. Curating the shelf a station
        // reads from is operational work, and the alternative was an operator waiting on an admin
        // to fix a mispronounced suburb.
        //
        // DELETE stays Admin across every library for the bookend reason: it is the one action
        // with no undo, and Archive already gives the operator the outcome they need.
        if (m != "DELETE" && (
                path.StartsWith("/api/voices", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/pronunciation-dictionaries", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/delivery-styles", StringComparison.OrdinalIgnoreCase)))
            return AuthorizationMiddleware.RoleOperator;

        // Triggering a bulletin is the operator's own act — it pairs with the /publish grant
        // above, which would otherwise let an operator put a bulletin to air but never make one.
        // It resolves a market (BroadcastHttpTrigger.ResolveMarketIdAsync) and the handler runs
        // EnsureMarketAccessAsync against it, so this IS market-scoped despite the flat route:
        // an operator can only generate for a station they are assigned to.
        if (m == "POST" && path.Equals("/api/broadcast", StringComparison.OrdinalIgnoreCase))
            return AuthorizationMiddleware.RoleOperator;

        // Everything else (POST /synthesize, DELETE /schedules/*, DELETE /jobs/*,
        // /editorial/*, global guidance + promote) → Admin.
        return AuthorizationMiddleware.RoleAdmin;
    }
}
