using System.Net;
using Azure.Data.Tables;
using Shouldly;
namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class GovernanceCrudE2ETests(E2EHttpClientFixture fixture)
{
    [Fact]
    public async Task GuidanceSafetyPriorityRoadsOperators_RoundTrip()
    {
        var market = $"e2e-gov-{Guid.NewGuid():N}"[..20];

        // Market-scoped guidance/safety/priority-road PUTs are guarded by a market-existence
        // 404 check, so the market must exist as a schedule first.
        await BackendE2ETestSupport.CreateScheduleAsync(fixture, market, $"gov-{market}");

        var globalGuidanceId = await UpsertGuidance(new { Scope = "Global", ScopeId = (string?)null, Type = "Editorial", Text = "Prefer clear traffic leads.", Enabled = true });
        var marketGuidanceId = await UpsertGuidance(new { Scope = "Market", ScopeId = market, Type = "Style", Text = "Use Gold Coast road names.", Enabled = true });

        await ExpectOk($"guidance?scope=Global");
        await ExpectOk($"markets/{market}/guidance");
        await ExpectOk($"guidance/inventory?markets={market}");
        await ExpectOk($"markets/{market}/guidance/composed");
        await ExpectOk($"markets/{market}/guidance/candidates");

        // Promotion requires a REAL candidate: the JobId must reference an entry in the
        // market's feedback corpus, so arbitrary text can no longer be minted into a directive
        // (wave finding — a ghost or absent job id used to produce a silent "promoted" row).
        // RejectJob no longer accepts/creates a FutureLesson. Seed the candidate store directly as
        // test data, then exercise the real promotion endpoint and its idempotency contract.
        var candidateJobId = await CreateBroadcastForMarketAsync(fixture, market);
        await BackendE2ETestSupport.WaitForJobStatusAsync(
            fixture, candidateJobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        var candidateId = await SeedGuidanceCandidateAsync(candidateJobId);
        string promotedId;
        using (var candidates = await fixture.Client.GetAsync($"markets/{market}/guidance/candidates"))
        {
            candidates.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await candidates.Content.ReadAsStringAsync()).ShouldContain(candidateJobId);
        }
        using (var promote = await fixture.Client.PostAsync("guidance/promote",
                   E2EHttpClientFixture.JsonContent(new { Scope = "Market", ScopeId = market, Type = "Editorial", Text = "Promoted E2E guidance", JobId = candidateJobId })))
        {
            promote.StatusCode.ShouldBe(HttpStatusCode.OK);
            promotedId = (await BackendE2ETestSupport.ReadJsonAsync(promote)).StringProp("id", "Id")!;
            promotedId.ShouldNotBeNullOrWhiteSpace();
        }

        // Idempotent promotion: promoting the same candidate again returns the SAME directive
        // (wave finding — a second promote must never air a duplicate).
        using (var promoteAgain = await fixture.Client.PostAsync("guidance/promote",
                   E2EHttpClientFixture.JsonContent(new { Scope = "Market", ScopeId = market, Type = "Editorial", Text = "Promoted E2E guidance", JobId = candidateJobId })))
        {
            promoteAgain.StatusCode.ShouldBe(HttpStatusCode.OK);
            var again = await BackendE2ETestSupport.ReadJsonAsync(promoteAgain);
            again.StringProp("id", "Id").ShouldBe(promotedId);
        }
        await DeleteGuidanceCandidateAsync(candidateId);
        using (var deletePromoted = await fixture.Client.DeleteAsync($"markets/{market}/guidance/{promotedId}"))
            deletePromoted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var deleteMarket = await fixture.Client.DeleteAsync($"markets/{market}/guidance/{marketGuidanceId}"))
            deleteMarket.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var deleteGlobal = await fixture.Client.DeleteAsync($"guidance/{globalGuidanceId}?scope=Global"))
            deleteGlobal.StatusCode.ShouldBe(HttpStatusCode.OK);

        var bannedId = await UpsertSafety(new { Kind = "Banned", Scope = "Global", ScopeId = (string?)null, Term = "blocked-test-word", Note = "E2E banned term" });
        var allowedId = await UpsertSafety(new { Kind = "Allowed", Scope = "Market", ScopeId = market, Term = "Coomera", Note = "Place-name exception" });
        await ExpectOk("safety/terms");
        using (var deleteBanned = await fixture.Client.DeleteAsync($"safety/terms/{bannedId}?scope=Global"))
            deleteBanned.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var deleteAllowed = await fixture.Client.DeleteAsync($"safety/terms/{allowedId}?scope=Market&scopeId={market}"))
            deleteAllowed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var putRoads = await fixture.Client.PutAsync($"markets/{market}/priority-roads",
                   E2EHttpClientFixture.JsonContent(new { Roads = new[] { new { Name = "M1 Pacific Motorway" }, new { Name = "Gold Coast Highway" } } })))
            putRoads.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var getRoads = await fixture.Client.GetAsync($"markets/{market}/priority-roads"))
        {
            getRoads.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await getRoads.Content.ReadAsStringAsync()).ShouldContain("M1 Pacific Motorway");
        }
        await ExpectOk($"priority-roads/inventory?markets={market}");

        await ExpectOk("operators/me");
        var objectId = Guid.NewGuid().ToString("N");
        using (var putOperator = await fixture.Client.PutAsync($"operators/{objectId}",
                   E2EHttpClientFixture.JsonContent(new { DisplayName = "E2E Operator", Markets = new[] { market } })))
            putOperator.StatusCode.ShouldBe(HttpStatusCode.OK);
        await ExpectOk("operators");
        using (var deleteOperator = await fixture.Client.DeleteAsync($"operators/{objectId}"))
            deleteOperator.StatusCode.ShouldBe(HttpStatusCode.OK);
    }


    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1";
    private const string CandidateStation = "E2E FM";

    private static async Task<string> SeedGuidanceCandidateAsync(string jobId)
    {
        var table = new TableClient(AzuriteConnection, "futureLessons");
        await table.CreateIfNotExistsAsync();

        string id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity
        {
            PartitionKey = CandidateStation.ToLowerInvariant(),
            RowKey = id,
            ["SourceJobId"] = jobId,
            ["Category"] = "PriorityOrdering",
            ["Text"] = "Always lead with the motorway closure on air.",
            ["CreatedAtUtc"] = now,
            ["LastSeenAtUtc"] = now,
            ["OccurrenceCount"] = 1,
            ["ExpiresAtUtc"] = now.AddHours(1),
            ["Status"] = "Candidate",
            ["Origin"] = "Operator"
        };
        await table.AddEntityAsync(entity);
        return id;
    }

    private static async Task DeleteGuidanceCandidateAsync(string id)
    {
        var table = new TableClient(AzuriteConnection, "futureLessons");
        await table.DeleteEntityAsync(CandidateStation.ToLowerInvariant(), id);
    }

    /// <summary>
    ///     Creates a broadcast job for a SPECIFIC market by using the market id as the Location:
    ///     MarketLocationResolver matches a free-text location against MarketId/Name/Station, so
    ///     the job's market is the seeded one and its station matches the candidate fixture.
    /// </summary>
    private static async Task<string> CreateBroadcastForMarketAsync(E2EHttpClientFixture fixture, string market)
    {
        var payload = new
        {
            TargetDurationSeconds = 30.0,
            Location = market,
            Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 35.0 },
            VoiceId = "oKDz3nEzNfwKIVvUwPs0",
            VoiceProvider = "ElevenLabs",
            VoiceModel = "eleven_v3",
            Content = new { Traffic = true, Weather = true, Fuel = true }
        };
        using var response = await fixture.Client.PostAsync("broadcast", E2EHttpClientFixture.JsonContent(payload));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await BackendE2ETestSupport.ReadJsonAsync(response);
        body.StringProp("status", "Status").ShouldBe("Queued");
        var jobId = body.StringProp("jobId", "JobId")!;
        jobId.ShouldNotBeNullOrWhiteSpace();
        return jobId;
    }

    private async Task<string> UpsertGuidance(object body)
    {
        using var response = await fixture.Client.PutAsync("guidance", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await BackendE2ETestSupport.ReadJsonAsync(response);
        return json.StringProp("id", "Id")!;
    }

    private async Task<string> UpsertSafety(object body)
    {
        using var response = await fixture.Client.PutAsync("safety/terms", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await BackendE2ETestSupport.ReadJsonAsync(response);
        return json.StringProp("id", "Id")!;
    }

    private async Task ExpectOk(string route)
    {
        using var response = await fixture.Client.GetAsync(route);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
