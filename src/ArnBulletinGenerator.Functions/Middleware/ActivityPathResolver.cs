using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Functions.Middleware;

/// <summary>
///     Maps an absolute request path (<c>HttpRequestData.Url.AbsolutePath</c>) to an audit
///     <c>(resource, entityId)</c> pair for <see cref="ActivityLoggingMiddleware" />. Routes are
///     irregular (the entity id is sometimes mid-path), so this is an ordered, first-match-wins
///     rule table rather than a generic segment-index heuristic. Fails closed: a path matching
///     no rule and not explicitly excluded returns <c>null</c> — add a rule here rather than
///     guess when a new admin route is added.
/// </summary>
internal static class ActivityPathResolver
{
    // Preview/remix/calibrate/synthesize clicks never persist a change — logging them would
    // mislabel a measurement/preview action as an edit. Checked before the rule table because
    // several of these paths would otherwise match a non-anchored rule below (e.g. a preview
    // under /jobs/{id}/preview would match the "jobs" rule).
    private static readonly Regex[] ExcludedSuffixes =
    [
        new Regex("/preview$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex("/remix$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex("/calibrate$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/synthesize",
        "/api/health",
        "/api/GetRoles",
        "/api/config/export",
        "/api/config/schema",
        "/api/config/imports/pending", // list, GET-only anyway
        "/api/activity", // the audit endpoint itself — never audit-log reads of the audit log
        "/api/metrics/validity-probe"
    };

    private static readonly (Regex Pattern, string Resource, int? EntityGroup)[] Rules =
    [
        (Rx(@"^/api/config/imports/pending/([^/]+)/(approve|reject)$"), "config-imports", 1),
        (Rx(@"^/api/config/import$"), "config", null),
        (Rx(@"^/api/markets/([^/]+)/priority-roads$"), "priority-roads", 1),
        (Rx(@"^/api/markets/([^/]+)/incident-priorities$"), "incident-priorities", 1),
        (Rx(@"^/api/markets/([^/]+)/filler-statements$"), "filler-statements", 1),
        (Rx(@"^/api/markets/([^/]+)/verified-pronunciations$"), "verified-pronunciations", 1),
        (Rx(@"^/api/markets/([^/]+)/playout-tray$"), "playout-tray", 1),
        (Rx(@"^/api/markets/([^/]+)/guidance(/.*)?$"), "guidance", 1),
        (Rx(@"^/api/guidance(/.*)?$"), "guidance", null),
        (Rx(@"^/api/schedules/([^/]+)$"), "schedules", 1),
        (Rx(@"^/api/delivery-styles/([^/]+)"), "delivery-styles", 1),
        (Rx(@"^/api/delivery-styles$"), "delivery-styles", null),
        // A playbook edit changes the instructions the writer is given, so it changes what goes to
        // air. That makes it exactly the class of change the activity log exists for.
        (Rx(@"^/api/playbooks/([^/]+)"), "playbooks", 1),
        (Rx(@"^/api/playbooks$"), "playbooks", null),
        (Rx(@"^/api/pronunciation-dictionaries/([^/]+)"), "pronunciation-dictionaries", 1),
        (Rx(@"^/api/pronunciation-dictionaries$"), "pronunciation-dictionaries", null),
        (Rx(@"^/api/voices/profiles/([^/]+)"), "voices/profiles", 1),
        (Rx(@"^/api/voices/profiles$"), "voices/profiles", null),
        (Rx(@"^/api/operators/([^/]+)$"), "operators", 1),
        // entityId null here (unlike every other per-id route above): the term's own id is a
        // worse browsing key than its market -- ActivityLoggingMiddleware's response-body
        // fallback resolves ScopeId (market) from the delete response instead, matching how
        // the PUT upsert below (no id in the URL at all) already resolves it.
        (Rx(@"^/api/safety/terms/([^/]+)$"), "safety-terms", null),
        (Rx(@"^/api/safety/terms$"), "safety-terms", null),
        (Rx(@"^/api/style-rules"), "style-rules", null),
        // Before the generic /jobs rule: the change this records lands on the DICTIONARY, so it
        // belongs beside the library's own edits. EntityGroup is null deliberately — the response
        // fallback resolves the dictionary id from the body, not the job id from the path.
        (Rx(@"^/api/jobs/([^/]+)/pronunciation-rules$"), "pronunciation-dictionaries", null),
        (Rx(@"^/api/jobs/([^/]+)"), "jobs", 1),
        (Rx(@"^/api/broadcast$"), "broadcast", null),
        // The Event Ledger's operator verdict — the ONE action that changes what goes to air
        // (an omitted road event is dropped from fetched evidence before corridor selection), so
        // "who took this off air" must be answerable. Market is the browsing key rather than the
        // event key, matching the safety-terms reasoning above; the event key is never lost,
        // because ActivityLogEntry stores the full request Path verbatim.
        (Rx(@"^/api/ledger/([^/]+)/events/([^/]+)/verdict$"), "ledger", 1),
        // A reviewer's attestation that they spot-checked a bulletin, keyed by that job.
        (Rx(@"^/api/spot-checks/([^/]+)$"), "spot-checks", 1),
        (Rx(@"^/api/bookends/([^/]+)"), "bookends", 1),
        (Rx(@"^/api/bookends$"), "bookends", null),
        (Rx(@"^/api/editorial/settings$"), "editorial-settings", null),
        // Dismissing a producer-feedback candidate deletes it; keyed by station like the
        // station-scoped guidance rows it sits beside.
        (Rx(@"^/api/stations/([^/]+)/guidance/candidates/([^/]+)$"), "guidance", 1),
    ];

    /// <summary>
    ///     Whether this path is DELIBERATELY not audited (a read, a measurement, a preview) as
    ///     opposed to merely unmapped. <see cref="Resolve" /> answers <c>null</c> for both, which
    ///     is why the audit-coverage invariant needs to tell them apart: a mutating route that is
    ///     neither mapped nor excluded is an oversight, and used to be silently unauditable.
    /// </summary>
    internal static bool IsExplicitlyExcluded(string absolutePath) =>
        ExcludedPaths.Contains(absolutePath) || ExcludedSuffixes.Any(rx => rx.IsMatch(absolutePath));

    internal static (string Resource, string? EntityId)? Resolve(string absolutePath)
    {
        if (IsExplicitlyExcluded(absolutePath))
            return null;

        foreach ((Regex pattern, string resource, int? entityGroup) in Rules)
        {
            var match = pattern.Match(absolutePath);
            if (!match.Success)
                continue;

            string? entityId = entityGroup.HasValue && match.Groups[entityGroup.Value].Success
                ? match.Groups[entityGroup.Value].Value
                : null;
            return (resource, entityId);
        }

        return null;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
