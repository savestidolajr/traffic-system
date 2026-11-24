using System.Net;
using System.Text.Json;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class RegenerationAndFallbackE2ETests(E2EHttpClientFixture fixture)
{
    [Fact]
    public async Task Reject_Regenerate_RejectToFallback_ExercisesOperatorLoop()
    {
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");

        using (var reject = await fixture.Client.PostAsync($"jobs/{jobId}/reject",
                   E2EHttpClientFixture.JsonContent(new { ReasonCode = "TimingLength", Notes = "Please tighten the M1 lead" })))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var rejected = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(20), "Rejected");
        rejected.ContainsText("Please tighten the M1 lead").ShouldBeTrue();

        using (var regenerate = await fixture.Client.PostAsync($"jobs/{jobId}/regenerate",
                   E2EHttpClientFixture.JsonContent(new { Context = "Lead with Smith Street Motorway", TargetDurationSec = 30.0 })))
        {
            regenerate.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var regenerated = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        regenerated.ContainsText("M1 Pacific Motorway").ShouldBeTrue();
        var regeneratedEtag = regenerated.StringProp("etag", "Etag");
        regeneratedEtag.ShouldNotBeNullOrWhiteSpace();

        using (var fallback = await fixture.Client.PostAsync($"jobs/{jobId}/reject-to-fallback",
                   E2EHttpClientFixture.JsonContent(new
                   {
                       ReasonCode = "ContentSelection",
                       Notes = "Use fallback for the test slot",
                       ExpectedEtag = regeneratedEtag
                   })))
        {
            fallback.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Rejecting to fallback covers the slot AND delivers it, so `FallbackUsed` only
        // exists inside the POST: `DeliverCoveredSlotAsync` approves then publishes before
        // the call returns (pinned in JobServiceTests). Waiting for the transient status
        // would poll a state the operator never sees. `Published` here is the assertion
        // that the audio reached playout rather than the record merely claiming coverage.
        var fallbackJob = await BackendE2ETestSupport.WaitForJobStatusAsync(
            fixture, jobId, TimeSpan.FromSeconds(20), "Published");
        fallbackJob.ContainsText("fallback").ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerate_RevoiceOnly_PutsEditedScriptToAirVerbatim()
    {
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");

        using (var reject = await fixture.Client.PostAsync($"jobs/{jobId}/reject",
                   E2EHttpClientFixture.JsonContent(new { ReasonCode = "RevisionRequested", Notes = "operator will re-voice an exact script" })))
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(20), "Rejected");

        // Distinctive, deliberately "unsupported" wording: the full LLM regenerate path would rewrite
        // it and the review stage would strip the editorialised opener. Re-voice must keep it verbatim.
        const string exact = "VERBATIM-MARKER-ZEBRA: here is your exact operator-written traffic read for the slot.";
        using (var regen = await fixture.Client.PostAsync($"jobs/{jobId}/regenerate",
                   E2EHttpClientFixture.JsonContent(new { RevoiceOnly = true, EditedScript = exact })))
            regen.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var done = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        done.ContainsText("VERBATIM-MARKER-ZEBRA").ShouldBeTrue();
        done.ContainsText("Re-voicing the existing script").ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerate_ExcludingIncident_DropsItFromTheOnAirSet()
    {
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        var initial = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        initial.ContainsText("M1 Pacific Motorway").ShouldBeTrue();

        using (var reject = await fixture.Client.PostAsync($"jobs/{jobId}/reject",
                   E2EHttpClientFixture.JsonContent(new { ReasonCode = "WrongOrMissingIncident", Notes = "drop the lead crash" })))
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(20), "Rejected");

        // fake-incident-m1-crash is the sole incident on the M1 Pacific Motorway corridor (top-ranked),
        // so excluding it drops that corridor from the regenerated on-air set.
        using (var regen = await fixture.Client.PostAsync($"jobs/{jobId}/regenerate",
                   E2EHttpClientFixture.JsonContent(new
                   {
                       EditedIncidents = new[]
                       {
                           new { IncidentId = "fake-incident-m1-crash", Included = false }
                       }
                   })))
            regen.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var regenerated = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        // The job log records exactly the exclusion the operator asked for — impossible before the fix
        // (the field was silently dropped by the backend).
        regenerated.ContainsText("Operator excluded 1 incident(s)").ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerate_WithTargetDurationOverride_PersistsNewSlotLength()
    {
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture); // default 30s slot
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");

        using (var reject = await fixture.Client.PostAsync($"jobs/{jobId}/reject",
                   E2EHttpClientFixture.JsonContent(new { ReasonCode = "TimingLength", Notes = "make it a 60 second read" })))
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(20), "Rejected");

        using (var regen = await fixture.Client.PostAsync($"jobs/{jobId}/regenerate",
                   E2EHttpClientFixture.JsonContent(new { TargetDurationSec = 60.0 })))
            regen.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The new target is applied to the job and persisted before it is queued — independent of the
        // (fake) audio fill outcome. Previously the override was ignored and the job stayed at 30s.
        await Eventually.UntilAsync(async () =>
        {
            var job = await BackendE2ETestSupport.GetJobAsync(fixture, jobId);
            var t = job.Prop("TargetDurationSec", "TargetDurationSeconds");
            return t is { ValueKind: JsonValueKind.Number } te && Math.Abs(te.GetDouble() - 60.0) < 0.01;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2), "Regenerated job did not adopt the 60s target");
    }
}
