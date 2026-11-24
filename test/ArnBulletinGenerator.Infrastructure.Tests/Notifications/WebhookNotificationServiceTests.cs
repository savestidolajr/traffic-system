using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Notifications;

public sealed class WebhookNotificationServiceTests
{
    private const string EndpointUrl = "https://webhook.example.com/api/notify";

    private static WebhookOptions DefaultOptions(bool enabled = true)
    {
        return new WebhookOptions
        {
            Enabled = enabled,
            EndpointUrl = EndpointUrl,
            OpsConsoleBaseUrl = "https://ops.example.com"
        };
    }

    private static (WebhookNotificationService Service, FakeHandler Handler) CreateSut(
        HttpStatusCode statusCode = HttpStatusCode.OK, bool enabled = true)
    {
        var opts = DefaultOptions(enabled);
        var handler = new FakeHandler(statusCode);
        var httpClient = new HttpClient(handler);
        var service = new WebhookNotificationService(Options.Create(opts), httpClient,
            NullLogger<WebhookNotificationService>.Instance);
        return (service, handler);
    }

    private static Job CreateTestJob(JobStatus status = JobStatus.WaitingApproval)
    {
        var job = Job.Create("job-1", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.SetStatus(status);
        return job;
    }

    // ── SendJobNotificationAsync ────────────────────────────────────

    [Fact]
    public async Task SendJobNotification_Success()
    {
        var (sut, handler) = CreateSut();
        var job = CreateTestJob();

        var result = await sut.SendJobNotificationAsync(job);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendJobNotification_PostsToCorrectUrl()
    {
        var (sut, handler) = CreateSut();

        await sut.SendJobNotificationAsync(CreateTestJob());

        handler.LastRequest!.RequestUri!.ToString().ShouldBe(EndpointUrl);
    }

    [Fact]
    public async Task SendJobNotification_PayloadContainsJobFields()
    {
        var (sut, handler) = CreateSut();
        var job = CreateTestJob();

        await sut.SendJobNotificationAsync(job);

        string body = handler.LastRequestBody!;
        body.ShouldContain("job-1");
        body.ShouldContain("Sydney");
        body.ShouldContain("2GB");
    }

    [Fact]
    public async Task SendJobNotification_ContentTypeHasNoCharset()
    {
        var (sut, handler) = CreateSut();

        await sut.SendJobNotificationAsync(CreateTestJob());

        var contentType = handler.LastRequest!.Content!.Headers.ContentType!;
        contentType.MediaType.ShouldBe("application/json");
        contentType.CharSet.ShouldBeNull("Webhook rejects charset=utf-8");
    }

    [Fact]
    public async Task SendJobNotification_Fails_OnHttpError()
    {
        var (sut, _) = CreateSut(HttpStatusCode.InternalServerError);

        var result = await sut.SendJobNotificationAsync(CreateTestJob());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("InternalServerError");
    }

    [Fact]
    public async Task SendJobNotification_SkipsWhenDisabled()
    {
        var (sut, handler) = CreateSut(enabled: false);

        var result = await sut.SendJobNotificationAsync(CreateTestJob());

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(0);
    }

    // ── SendAlertNotificationAsync ──────────────────────────────────

    [Fact]
    public async Task SendAlertNotification_Success()
    {
        var (sut, handler) = CreateSut();

        var result = await sut.SendAlertNotificationAsync("Sydney", "2GB", "Test alert", AlertSeverity.Warning);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequestBody!.ShouldContain("Sydney");
        handler.LastRequestBody!.ShouldContain("Test alert");
    }

    [Fact]
    public async Task SendAlertNotification_IncludesSeverityIcon()
    {
        var (sut, handler) = CreateSut();

        await sut.SendAlertNotificationAsync("M", "S", "msg", AlertSeverity.Critical);

        handler.LastRequestBody!.ShouldContain("Critical Alert");
    }
    // ── Suppressed statuses (DefaultKind → Suppressed) ─────────────

    [Theory]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.FallbackUsed)]
    public async Task SendJobNotification_SuppressedStatus_NoHttpRequest(JobStatus status)
    {
        var (sut, handler) = CreateSut();
        var job = CreateTestJob(status);

        var result = await sut.SendJobNotificationAsync(job);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(0);
    }

    // ── WaitingApproval default kind → ReadyForReview ──────────────

    [Fact]
    public async Task SendJobNotification_WaitingApproval_NullKind_SendsReadyForReview()
    {
        var (sut, handler) = CreateSut();
        var job = CreateTestJob(JobStatus.WaitingApproval);

        var result = await sut.SendJobNotificationAsync(job);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(1);

        var root = JsonDocument.Parse(handler.LastRequestBody!).RootElement;

        // New card fields
        root.TryGetProperty("card", out _).ShouldBeTrue("payload must contain 'card' object");
        root.GetProperty("kind").GetString().ShouldBe("ReadyForReview");
        root.GetProperty("schemaVersion").GetString().ShouldBe("3.0");

        // Legacy fields still present
        root.TryGetProperty("title", out _).ShouldBeTrue();
        root.TryGetProperty("previewText", out _).ShouldBeTrue();
        root.TryGetProperty("url", out _).ShouldBeTrue();
    }

    // ── Explicit kind overrides status-derived default ──────────────

    [Fact]
    public async Task SendJobNotification_FallbackUsed_ExplicitKind_OverridesSuppressed()
    {
        var (sut, handler) = CreateSut();
        var job = CreateTestJob(JobStatus.FallbackUsed);

        var result = await sut.SendJobNotificationAsync(job, NotificationKind.MissedDeadlineClosure);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(1);

        var root = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        root.GetProperty("kind").GetString().ShouldBe("MissedDeadlineClosure");
        root.TryGetProperty("card", out var card).ShouldBeTrue();
        card.GetProperty("type").GetString().ShouldBe("AdaptiveCard");
    }

    // ── Alert payload unchanged ─────────────────────────────────────

    [Fact]
    public async Task SendAlertNotification_HasSchemaVersion_NoCardField()
    {
        var (sut, handler) = CreateSut();

        await sut.SendAlertNotificationAsync("M", "S", "msg", AlertSeverity.Warning);

        var root = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        root.GetProperty("schemaVersion").GetString().ShouldBe("3.0");
        root.TryGetProperty("card", out _).ShouldBeFalse("alert payload must not contain 'card'");
    }

    // ── FakeHandler ─────────────────────────────────────────────────

    private sealed class FakeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(statusCode) { Content = new StringContent("ok") };
        }
    }
}
