using System.Net;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class VoiceAndContentLibraryE2ETests(E2EHttpClientFixture fixture)
{
    [Fact]
    public async Task VoiceDeliveryPronunciationAndSynthesis_RoundTrip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var voices = await fixture.Client.GetAsync("voices"))
        {
            voices.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = await voices.Content.ReadAsStringAsync();
            json.ShouldContain("Fake AU Male VO");
            json.ShouldContain("Fake AU Male Casual");
        }

        var profileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, suffix);
        using (var listProfiles = await fixture.Client.GetAsync("voices/profiles"))
            listProfiles.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var getProfile = await fixture.Client.GetAsync($"voices/profiles/{profileId}"))
            getProfile.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var preview = await fixture.Client.PostAsync("voices/profiles/preview",
                   E2EHttpClientFixture.JsonContent(new
                   {
                       BaseVoiceId = "oKDz3nEzNfwKIVvUwPs0",
                       Model = "eleven_v3",
                       Settings = new { Speed = 1.0, Stability = 0.5, SimilarityBoost = 0.9, Style = 0.1, UseSpeakerBoost = true }
                   })))
            preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        string remixedId;
        using (var remix = await fixture.Client.PostAsync("voices/profiles/remix",
                   E2EHttpClientFixture.JsonContent(new { BaseVoiceId = "oKDz3nEzNfwKIVvUwPs0", VoiceDescription = "slightly brighter", PromptStrength = 0.3 })))
        {
            remix.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await BackendE2ETestSupport.ReadJsonAsync(remix);
            remixedId = body.StringProp("generatedVoiceId", "GeneratedVoiceId")!;
            remixedId.ShouldStartWith("fake-remix-");
        }
        using (var updateProfile = await fixture.Client.PutAsync($"voices/profiles/{profileId}",
                   E2EHttpClientFixture.JsonContent(new
                   {
                       Name = $"E2E Hudson Updated {suffix}",
                       Description = "Updated by backend E2E",
                       Model = "eleven_v3",
                       Settings = new { Speed = 1.0, Stability = 0.55, SimilarityBoost = 0.9, Style = 0.2, UseSpeakerBoost = true },
                       ChangeNote = "E2E update",
                       BaseVoiceName = "Fake AU Male VO",
                       RemixedVoiceId = remixedId,
                       RemixDescription = "Adopted fake remix"
                   })))
            updateProfile.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var restoreProfile = await fixture.Client.PostAsync($"voices/profiles/{profileId}/versions/1/restore", E2EHttpClientFixture.JsonContent(new { })))
            restoreProfile.StatusCode.ShouldBe(HttpStatusCode.OK);

        var styleId = await BackendE2ETestSupport.CreateDeliveryStyleAsync(fixture, suffix);
        using (var previewStyle = await fixture.Client.PostAsync("delivery-styles/preview",
                   E2EHttpClientFixture.JsonContent(new { Policy = new { ToneDescription = "Urgent but calm", AllowedTags = new[] { "[urgent]" }, Intensity = 2 }, BaseVoiceId = "oKDz3nEzNfwKIVvUwPs0", Intent = "read it with urgency" })))
            previewStyle.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var updateStyle = await fixture.Client.PutAsync($"delivery-styles/{styleId}",
                   E2EHttpClientFixture.JsonContent(new { Name = $"E2E Style Updated {suffix}", Description = "Updated", Policy = new { ToneDescription = "Calm", AllowedTags = new[] { "[calm]" }, Intensity = 1 }, ChangeNote = "E2E update" })))
            updateStyle.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var restoreStyle = await fixture.Client.PostAsync($"delivery-styles/{styleId}/versions/1/restore", E2EHttpClientFixture.JsonContent(new { })))
            restoreStyle.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var archiveStyle = await fixture.Client.PostAsync($"delivery-styles/{styleId}/archive", E2EHttpClientFixture.JsonContent(new { })))
            archiveStyle.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var unarchiveStyle = await fixture.Client.PostAsync($"delivery-styles/{styleId}/unarchive", E2EHttpClientFixture.JsonContent(new { })))
            unarchiveStyle.StatusCode.ShouldBe(HttpStatusCode.OK);

        var dictId = await BackendE2ETestSupport.CreatePronunciationDictionaryAsync(fixture, suffix);
        using (var previewDict = await fixture.Client.PostAsync($"pronunciation-dictionaries/{dictId}/preview",
                   E2EHttpClientFixture.JsonContent(new { BaseVoiceId = "oKDz3nEzNfwKIVvUwPs0", Model = "eleven_v3", SampleText = "Coomera traffic update." })))
            previewDict.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var updateDict = await fixture.Client.PutAsync($"pronunciation-dictionaries/{dictId}",
                   E2EHttpClientFixture.JsonContent(new { Name = $"E2E Dictionary Updated {suffix}", Description = "Updated", Rules = new[] { new { Type = "alias", StringToReplace = "Nerang", Replacement = "Ne-rang", CaseSensitive = false, WordBoundaries = true } }, ChangeNote = "E2E update" })))
            updateDict.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var restoreDict = await fixture.Client.PostAsync($"pronunciation-dictionaries/{dictId}/versions/1/restore", E2EHttpClientFixture.JsonContent(new { })))
            restoreDict.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var archiveDict = await fixture.Client.PostAsync($"pronunciation-dictionaries/{dictId}/archive", E2EHttpClientFixture.JsonContent(new { })))
            archiveDict.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var unarchiveDict = await fixture.Client.PostAsync($"pronunciation-dictionaries/{dictId}/unarchive", E2EHttpClientFixture.JsonContent(new { })))
            unarchiveDict.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var synth = await fixture.Client.PostAsync("synthesize",
                   E2EHttpClientFixture.JsonContent(new { Script = "Traffic update.", TargetDurationSeconds = 30.0, VoiceId = "oKDz3nEzNfwKIVvUwPs0", Model = "eleven_v3", Stability = 0.5, SimilarityBoost = 0.9, Style = 0.1, UseSpeakerBoost = true, Speed = 1.0, Market = "goldcoast" })))
        {
            synth.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await synth.Content.ReadAsStringAsync()).ShouldContain("durationSec");
        }

        using (var calibration = await fixture.Client.PostAsync("voices/oKDz3nEzNfwKIVvUwPs0/calibrate", E2EHttpClientFixture.JsonContent(new { })))
        {
            calibration.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await calibration.Content.ReadAsStringAsync()).ShouldContain("syllablesPerSecond");
        }
    }
}
