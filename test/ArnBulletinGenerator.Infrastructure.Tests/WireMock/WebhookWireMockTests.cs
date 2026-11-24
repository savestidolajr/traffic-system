using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     WireMock integration tests for the webhook notification service.
///     Exercises the real HTTP POST client with a stubbed webhook endpoint.
/// </summary>
public sealed class WebhookWireMockTests : WireMockTestBase
{
    private const string WebhookPath = "/webhook";

    protected override Task OnInitializedAsync()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(WebhookPath))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"accepted": true}"""));

        return Task.CompletedTask;
    }

    private static Job CreateTestJob()
    {
        var jobResult = Job.Create(
            "job-test-123",
            "KIIS1011",
            "Melbourne",
            30.0,
            new DateTime(2026, 5, 21, 6, 30, 0, DateTimeKind.Utc));

        if (!jobResult.IsSuccess)
            throw new InvalidOperationException($"Failed to create test job: {jobResult.Error}");

        var job = jobResult.Value;
        job.SetStatus(JobStatus.WaitingApproval);
        job.ScriptText = Option<string>.Some("Good morning, Melbourne. Traffic update...");
        job.AudioUrl = Option<string>.Some("https://example.com/audio.wav");
        job.MeasuredDurationSec = Option<double>.Some(28.5);
        return job;
    }

    [Fact]
    public async Task SendJobNotificationAsync_Succeeds()
    {
        var notifier = Services.GetRequiredService<INotificationService>();
        var job = CreateTestJob();

        var result = await notifier.SendJobNotificationAsync(job, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
    }

    [Fact]
    public async Task SendJobNotificationAsync_IncludesJobDetailsInPayload()
    {
        var notifier = Services.GetRequiredService<INotificationService>();
        var job = CreateTestJob();

        var result = await notifier.SendJobNotificationAsync(job, null, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var body = LastRequestBody();
        Assert.Contains("job-test-123", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Melbourne", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KIIS1011", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAlertNotificationAsync_Succeeds()
    {
        var notifier = Services.GetRequiredService<INotificationService>();

        var result = await notifier.SendAlertNotificationAsync(
            "Melbourne", "KIIS1011", "Pipeline failure detected",
            AlertSeverity.Critical, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
    }

    [Fact]
    public async Task SendJobNotificationAsync_ReturnsFailure_WhenWebhookReturns500()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(WebhookPath))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        var notifier = Services.GetRequiredService<INotificationService>();
        var job = CreateTestJob();

        var result = await notifier.SendJobNotificationAsync(job, null, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
