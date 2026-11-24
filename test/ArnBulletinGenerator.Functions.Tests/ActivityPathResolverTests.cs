using Shouldly;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ActivityPathResolverTests
{
    [Theory]
    [InlineData("/api/config/imports/pending/adelaide/approve", "config-imports", "adelaide")]
    [InlineData("/api/config/imports/pending/adelaide/reject", "config-imports", "adelaide")]
    [InlineData("/api/config/import", "config", null)]
    [InlineData("/api/markets/adelaide/priority-roads", "priority-roads", "adelaide")]
    [InlineData("/api/markets/adelaide/filler-statements", "filler-statements", "adelaide")]
    [InlineData("/api/markets/adelaide/playout-tray", "playout-tray", "adelaide")]
    [InlineData("/api/markets/adelaide/guidance", "guidance", "adelaide")]
    [InlineData("/api/markets/adelaide/guidance/composed", "guidance", "adelaide")]
    [InlineData("/api/guidance", "guidance", null)]
    [InlineData("/api/guidance/abc123", "guidance", null)]
    [InlineData("/api/schedules/adelaide", "schedules", "adelaide")]
    [InlineData("/api/delivery-styles/style-1", "delivery-styles", "style-1")]
    [InlineData("/api/delivery-styles/style-1/restore/3", "delivery-styles", "style-1")]
    [InlineData("/api/delivery-styles", "delivery-styles", null)]
    [InlineData("/api/pronunciation-dictionaries/dict-1", "pronunciation-dictionaries", "dict-1")]
    [InlineData("/api/pronunciation-dictionaries", "pronunciation-dictionaries", null)]
    [InlineData("/api/voices/profiles/vp-1", "voices/profiles", "vp-1")]
    [InlineData("/api/voices/profiles", "voices/profiles", null)]
    [InlineData("/api/operators/op-1", "operators", "op-1")]
    [InlineData("/api/safety/terms/term-1", "safety-terms", null)]
    [InlineData("/api/safety/terms", "safety-terms", null)]
    [InlineData("/api/style-rules", "style-rules", null)]
    [InlineData("/api/jobs/job-1", "jobs", "job-1")]
    [InlineData("/api/jobs/job-1/approve", "jobs", "job-1")]
    // Files under the DICTIONARY it changed, not the job it was made from; the entity id comes
    // from the response body, so the path resolves none.
    [InlineData("/api/jobs/job-1/pronunciation-rules", "pronunciation-dictionaries", null)]
    [InlineData("/api/broadcast", "broadcast", null)]
    // Excluded — preview/remix/calibrate never persist a change, so they must never
    // be logged even though the underlying path would otherwise match a rule above.
    [InlineData("/api/voices/profiles/vp-1/preview", null, null)]
    [InlineData("/api/jobs/job-1/remix", null, null)]
    [InlineData("/api/voices/calibrate", null, null)]
    [InlineData("/api/synthesize", null, null)]
    [InlineData("/api/health", null, null)]
    [InlineData("/api/GetRoles", null, null)]
    [InlineData("/api/config/export", null, null)]
    [InlineData("/api/config/schema", null, null)]
    [InlineData("/api/config/imports/pending", null, null)]
    [InlineData("/api/activity", null, null)]
    // The Desk's only on-air-changing action: keyed by market, event key preserved in the Path.
    [InlineData("/api/ledger/sydney/events/evt-1/verdict", "ledger", "sydney")]
    [InlineData("/api/spot-checks/job-1", "spot-checks", "job-1")]
    [InlineData("/api/bookends", "bookends", null)]
    [InlineData("/api/bookends/bk-1", "bookends", "bk-1")]
    [InlineData("/api/bookends/bk-1/archive", "bookends", "bk-1")]
    [InlineData("/api/editorial/settings", "editorial-settings", null)]
    [InlineData("/api/stations/kiis-1065/guidance/candidates/c-1", "guidance", "kiis-1065")]
    [InlineData("/api/metrics/validity-probe", null, null)]
    // Unrecognized — fail closed, never guess.
    [InlineData("/api/some-future-route/foo", null, null)]
    public void Resolve_MapsPathToExpectedResourceAndEntityId(string path, string? expectedResource, string? expectedEntityId)
    {
        var resolved = ActivityPathResolver.Resolve(path);

        if (expectedResource is null)
        {
            resolved.ShouldBeNull();
            return;
        }

        resolved.ShouldNotBeNull();
        resolved.Value.Resource.ShouldBe(expectedResource);
        resolved.Value.EntityId.ShouldBe(expectedEntityId);
    }

    /// <summary>
    ///     Whole-surface audit coverage. The resolver fails CLOSED, which is safe but silent: a new
    ///     mutating route simply never appears in the activity log, and nothing tells you. That is
    ///     exactly how the Event Ledger shipped — POST .../verdict changed what went to air with no
    ///     audit trail, while a mere schedule tweak was logged. This asserts the property the doc
    ///     comment on the rule table only asked for in prose.
    /// </summary>
    [Fact]
    public void EveryMutatingRoute_ResolvesToAnAuditResource()
    {
        var routes = RegisteredRoutes.All();
        routes.Count.ShouldBeGreaterThan(80);

        // Reads change nothing. Everything else is a mutation and must be attributable, unless
        // it is one of the measurement/preview routes the resolver deliberately excludes.
        var unaudited = routes
            .Where(r => r.Method is not ("GET" or "HEAD"))
            .Select(r => r.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => ActivityPathResolver.Resolve(path) is null
                && !ActivityPathResolver.IsExplicitlyExcluded(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        unaudited.ShouldBeEmpty(
            "these mutating routes resolve to no audit resource, so they write nothing to the " +
            "activity log — add a rule to ActivityPathResolver (or an explicit exclusion if the " +
            $"route genuinely persists nothing):{Environment.NewLine}" +
            string.Join(Environment.NewLine, unaudited));
    }
}
