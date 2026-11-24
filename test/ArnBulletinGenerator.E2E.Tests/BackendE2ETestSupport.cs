using System.Net;
using System.Text.Json;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

internal static class BackendE2ETestSupport
{
    public static object BroadcastPayload(double duration = 30) => new
    {
        TargetDurationSeconds = duration,
        Location = "Gold Coast",
        Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 35.0 },
        // No VoiceName: the backend resolves the display name from the voice provider and a
        // caller-supplied name never overrides one the provider knows, so sending it would be
        // dead weight that silently disagrees with the fake catalogue.
        VoiceId = "oKDz3nEzNfwKIVvUwPs0",
        VoiceProvider = "ElevenLabs",
        VoiceModel = "eleven_v3",
        Content = new { Traffic = true, Weather = true, Fuel = true }
    };

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldNotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    public static async Task<string> CreateBroadcastAsync(E2EHttpClientFixture fixture)
    {
        using var response = await fixture.Client.PostAsync("broadcast", E2EHttpClientFixture.JsonContent(BroadcastPayload()));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await ReadJsonAsync(response);
        body.StringProp("jobId", "JobId").ShouldNotBeNullOrWhiteSpace();
        body.StringProp("status", "Status").ShouldBe("Queued");
        return body.StringProp("jobId", "JobId")!;
    }

    public static async Task<JsonElement> GetJobAsync(E2EHttpClientFixture fixture, string jobId)
    {
        using var response = await fixture.Client.GetAsync($"jobs/{jobId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    public static async Task<JsonElement> WaitForJobStatusAsync(
        E2EHttpClientFixture fixture,
        string jobId,
        TimeSpan timeout,
        params string[] statuses)
    {
        JsonElement latest = default;
        await Eventually.UntilAsync(async () =>
        {
            latest = await GetJobAsync(fixture, jobId);
            var status = latest.StringProp("status", "Status");
            return status is not null && statuses.Any(s => SameStatus(status, s));
        }, timeout, TimeSpan.FromSeconds(2), $"Job {jobId} did not reach {string.Join("/", statuses)}. Latest: {latest}");
        return latest;
    }

    public static async Task<string> CreateVoiceProfileAsync(E2EHttpClientFixture fixture, string suffix)
    {
        var body = new
        {
            Name = $"E2E Hudson {suffix}",
            Description = "Created by backend E2E",
            BaseVoiceId = "oKDz3nEzNfwKIVvUwPs0",
            BaseVoiceName = "Fake AU Male VO",
            Model = "eleven_v3",
            Settings = new { Speed = 1.0, Stability = 0.5, SimilarityBoost = 0.9, Style = 0.1, UseSpeakerBoost = true },
            ChangeNote = "Initial E2E version"
        };
        using var response = await fixture.Client.PostAsync("voices/profiles", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.StringProp("id", "Id").ShouldNotBeNullOrWhiteSpace();
        return json.StringProp("id", "Id")!;
    }

    /// <summary>
    ///     Seeds a minimal (disabled) schedule so a market exists for the market-existence 404
    ///     guards on market-scoped guidance/safety/priority-road PUTs. Disabled so the poller
    ///     never fires a real pipeline for the seed market. Returns the created voice profile id.
    /// </summary>
    public static async Task<string> CreateScheduleAsync(E2EHttpClientFixture fixture, string market, string voiceSuffix)
    {
        var voiceProfileId = await CreateVoiceProfileAsync(fixture, voiceSuffix);
        var body = new
        {
            Name = "E2E Seed Schedule",
            Station = "E2E FM",
            Timezone = "Australia/Brisbane",
            CronExpression = "0 7 * * 1-5",
            Enabled = false,
            TargetDurationSeconds = 30.0,
            NotifyBeforePlayoutMinutes = 5,
            EscalationBeforePlayoutMinutes = 2,
            Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 35.0 },
            Content = new { Traffic = true, Weather = false, Fuel = false },
            VoiceProfileId = voiceProfileId
        };
        using var response = await fixture.Client.PutAsync($"schedules/{market}", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return voiceProfileId;
    }

    public static async Task<string> CreateDeliveryStyleAsync(E2EHttpClientFixture fixture, string suffix)
    {
        var body = new
        {
            Name = $"E2E Style {suffix}",
            Description = "Created by backend E2E",
            Policy = new { ToneDescription = "Warm but urgent radio delivery", AllowedTags = new[] { "[calm]", "[urgent]" }, Intensity = 2 },
            ChangeNote = "Initial E2E version"
        };
        using var response = await fixture.Client.PostAsync("delivery-styles", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.StringProp("id", "Id").ShouldNotBeNullOrWhiteSpace();
        return json.StringProp("id", "Id")!;
    }

    public static async Task<string> CreatePronunciationDictionaryAsync(E2EHttpClientFixture fixture, string suffix)
    {
        var body = new
        {
            Name = $"E2E Dictionary {suffix}",
            Description = "Created by backend E2E",
            Rules = new[] { new { Type = "alias", StringToReplace = "Coomera", Replacement = "Koo-murra", CaseSensitive = false, WordBoundaries = true } },
            ChangeNote = "Initial E2E version"
        };
        using var response = await fixture.Client.PostAsync("pronunciation-dictionaries", E2EHttpClientFixture.JsonContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.StringProp("id", "Id").ShouldNotBeNullOrWhiteSpace();
        return json.StringProp("id", "Id")!;
    }

    private static bool SameStatus(string actual, string expected) =>
        NormalizeStatus(actual) == NormalizeStatus(expected);

    private static string NormalizeStatus(string status) =>
        status.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
}
