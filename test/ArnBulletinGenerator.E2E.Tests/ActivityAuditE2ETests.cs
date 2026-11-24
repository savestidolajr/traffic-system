using System.Net;
using System.Text.Json;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     Proves the cross-cutting <c>ActivityLoggingMiddleware</c> actually records changes against
///     the real Functions host: a schedule PUT that changes <c>VoiceProfileId</c> shows up in
///     <c>GET /api/activity?resource=schedules&amp;entityId={market}</c> with the caller's identity
///     and the submitted body, newest first.
/// </summary>
[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ActivityAuditE2ETests(E2EHttpClientFixture fixture)
{
    [Fact(Timeout = 90_000)]
    public async Task ScheduleVoiceChange_IsRecordedInActivityLog_NewestFirst()
    {
        var market = $"e2e-activity-{Guid.NewGuid():N}"[..24];
        var voiceA = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, "activity-a");
        var voiceB = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, "activity-b");

        var createBody = new
        {
            Name = "E2E Activity Schedule",
            Station = "E2E FM",
            Timezone = "Australia/Brisbane",
            CronExpression = "0 7 * * 1-5",
            Enabled = true,
            TargetDurationSeconds = 30.0,
            NotifyBeforePlayoutMinutes = 5,
            EscalationBeforePlayoutMinutes = 2,
            Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 35.0 },
            Content = new { Traffic = true, Weather = true, Fuel = false },
            VoiceProfileId = voiceA
        };
        using (var create = await fixture.Client.PutAsync($"schedules/{market}", E2EHttpClientFixture.JsonContent(createBody)))
            create.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Baseline after creation (the create PUT is itself a logged mutation) so the assertions
        // below are a delta, not an absolute count tied to how many PUTs setup happens to issue.
        int baselineCount = (await GetActivityItemsAsync(market)).GetArrayLength();

        using (var updateToB = await fixture.Client.PutAsync(
                   $"schedules/{market}", E2EHttpClientFixture.JsonContent(new { VoiceProfileId = voiceB })))
            updateToB.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement afterFirst = default;
        await Eventually.UntilAsync(async () =>
        {
            afterFirst = await GetActivityItemsAsync(market);
            return afterFirst.GetArrayLength() == baselineCount + 1;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1),
            $"Activity log did not record the first schedule change for {market}.");

        var newest = afterFirst[0];
        newest.StringProp("actorName", "ActorName").ShouldBe("Local Dev");
        newest.StringProp("method", "Method").ShouldBe("PUT");
        newest.StringProp("resource", "Resource").ShouldBe("schedules");
        newest.StringProp("entityId", "EntityId").ShouldBe(market);
        string? newestBody = newest.StringProp("requestBodyJson", "RequestBodyJson");
        newestBody.ShouldNotBeNullOrWhiteSpace();
        newestBody.ShouldContain(voiceB);

        using (var updateToA = await fixture.Client.PutAsync(
                   $"schedules/{market}", E2EHttpClientFixture.JsonContent(new { VoiceProfileId = voiceA })))
            updateToA.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement afterSecond = default;
        await Eventually.UntilAsync(async () =>
        {
            afterSecond = await GetActivityItemsAsync(market);
            return afterSecond.GetArrayLength() == baselineCount + 2;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1),
            $"Activity log did not record the second schedule change for {market}.");

        // Newest first: index 0 is the most recent PUT (back to voiceA), index 1 the one before it.
        string? secondBody = afterSecond[0].StringProp("requestBodyJson", "RequestBodyJson");
        string? firstBody = afterSecond[1].StringProp("requestBodyJson", "RequestBodyJson");
        secondBody.ShouldNotBeNullOrWhiteSpace();
        firstBody.ShouldNotBeNullOrWhiteSpace();
        secondBody.ShouldContain(voiceA);
        firstBody.ShouldContain(voiceB);

        using (var delete = await fixture.Client.DeleteAsync($"schedules/{market}"))
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact(Timeout = 60_000)]
    public async Task SafetyTermUpsertAndDelete_BothResolveToMarketViaResponseFallback()
    {
        // PUT /api/safety/terms and DELETE /api/safety/terms/{id} both carry the market in the
        // BODY/response, not the URL -- proves ActivityLoggingMiddleware's response-body
        // fallback (ScopeId) against the real host, not just the in-process unit test.
        var market = $"e2e-safety-{Guid.NewGuid():N}"[..20];

        // A Market-scoped safety term is guarded by a market-existence 404 check, so the
        // market must exist as a schedule first. (Logged under resource=schedules/voices, not
        // safety-terms, so it does not perturb the safety-terms activity assertions below.)
        await BackendE2ETestSupport.CreateScheduleAsync(fixture, market, $"safety-{market}");

        string termId;
        using (var upsert = await fixture.Client.PutAsync("safety/terms", E2EHttpClientFixture.JsonContent(
                   new { Kind = "Banned", Scope = "Market", ScopeId = market, Term = "e2e-blocked-word" })))
        {
            upsert.StatusCode.ShouldBe(HttpStatusCode.OK);
            termId = (await BackendE2ETestSupport.ReadJsonAsync(upsert)).StringProp("id", "Id")!;
        }

        JsonElement afterUpsert = default;
        await Eventually.UntilAsync(async () =>
        {
            afterUpsert = await GetActivityItemsAsync("safety-terms", market);
            return afterUpsert.GetArrayLength() == 1;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1),
            $"Activity log did not record the safety-term upsert for market {market} under entityId={market}.");
        string? upsertBody = afterUpsert[0].StringProp("requestBodyJson", "RequestBodyJson");
        upsertBody.ShouldNotBeNullOrWhiteSpace();
        upsertBody.ShouldContain("e2e-blocked-word");

        using (var delete = await fixture.Client.DeleteAsync($"safety/terms/{termId}?scope=Market&scopeId={market}"))
            delete.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Eventually.UntilAsync(async () =>
        {
            var afterDelete = await GetActivityItemsAsync("safety-terms", market);
            return afterDelete.GetArrayLength() == 2 &&
                   afterDelete[0].StringProp("method", "Method") == "DELETE";
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1),
            $"Activity log did not record the safety-term delete for market {market} under entityId={market} " +
            "(the delete's entityId should fall back to ScopeId from the response, same as the upsert).");
    }

    private Task<JsonElement> GetActivityItemsAsync(string market) => GetActivityItemsAsync("schedules", market);

    private async Task<JsonElement> GetActivityItemsAsync(string resource, string entityId)
    {
        using var response = await fixture.Client.GetAsync($"activity?resource={resource}&entityId={entityId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await BackendE2ETestSupport.ReadJsonAsync(response);
        return json.Prop("items", "Items")!.Value;
    }
}
