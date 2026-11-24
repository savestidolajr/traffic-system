using System.Net;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class BroadcastPipelineE2ETests(E2EHttpClientFixture fixture)
{
    [Fact]
    public async Task Broadcast_Approve_Publish_CompletesFakePipeline()
    {
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        var waiting = await BackendE2ETestSupport.WaitForJobStatusAsync(
            fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");

        waiting.ContainsText("M1 Pacific Motorway").ShouldBeTrue();
        waiting.ContainsText("Smith Street Motorway").ShouldBeTrue();
        waiting.ContainsText("Gold Coast Highway").ShouldBeTrue();
        waiting.ContainsText("fake-incident-").ShouldBeTrue();
        waiting.ContainsText("audio").ShouldBeTrue();
        waiting.ContainsText("log").ShouldBeTrue();

        // Approve and publish bind the decision to the exact revision the operator
        // reviewed: both endpoints require ExpectedEtag and answer 428 without it, so
        // an approval can never attach to a bulletin nobody actually looked at. Prove
        // the precondition is mandatory on the wire, not merely honoured when supplied
        // — a silent relaxation back to "optional" is the regression that matters.
        using (var noEtag = await fixture.Client.PostAsync($"jobs/{jobId}/approve", E2EHttpClientFixture.JsonContent(new { })))
        {
            noEtag.StatusCode.ShouldBe((HttpStatusCode)428);
        }

        var waitingEtag = waiting.StringProp("etag", "Etag");
        waitingEtag.ShouldNotBeNullOrWhiteSpace();

        using (var approve = await fixture.Client.PostAsync(
            $"jobs/{jobId}/approve", E2EHttpClientFixture.JsonContent(new { ExpectedEtag = waitingEtag })))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var approved = await BackendE2ETestSupport.WaitForJobStatusAsync(
            fixture, jobId, TimeSpan.FromSeconds(20), "Approved");
        approved.ContainsText("approval").ShouldBeTrue();

        // Approving rewrote the row, so publish must assert the *new* revision.
        var approvedEtag = approved.StringProp("etag", "Etag");
        approvedEtag.ShouldNotBeNullOrWhiteSpace();

        using (var publish = await fixture.Client.PostAsync(
            $"jobs/{jobId}/publish", E2EHttpClientFixture.JsonContent(new { ExpectedEtag = approvedEtag })))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var published = await BackendE2ETestSupport.WaitForJobStatusAsync(
            fixture, jobId, TimeSpan.FromSeconds(20), "Published");
        published.ContainsText("publish").ShouldBeTrue();

        // A Published job is intentionally non-deletable — the lifecycle guard protects a
        // bulletin that has gone to playout. Deleting it must be rejected. DELETE now carries
        // the same revision precondition as approve/publish (428 without a body), so send the
        // published revision's etag to reach the status guard it exercises.
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"jobs/{jobId}")
        {
            Content = E2EHttpClientFixture.JsonContent(new { ExpectedEtag = published.StringProp("etag", "Etag") })
        };
        using (var delete = await fixture.Client.SendAsync(deleteRequest))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }
}
