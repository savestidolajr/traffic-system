using System.Net;
using Azure.Storage.Queues;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class QueueFailureE2ETests(E2EHttpClientFixture fixture)
{
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1";

    [Fact]
    public async Task MalformedQueueMessage_DoesNotBreakSubsequentBroadcast()
    {
        var queue = new QueueClient(AzuriteConnection, "broadcast-queue");
        await queue.CreateIfNotExistsAsync();
        await queue.SendMessageAsync("{ this is not valid broadcast queue json");

        await Eventually.UntilAsync(async () =>
        {
            using var response = await fixture.AnonymousClient.GetAsync("health");
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2), "Host did not stay healthy after malformed queue message.");

        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        var job = await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");
        job.ContainsText("M1 Pacific Motorway").ShouldBeTrue();
    }
}
