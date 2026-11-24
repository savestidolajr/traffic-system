using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Notifications;

[Trait("Category", "Integration")]
public sealed class WebhookNotificationServiceIntegrationTests(ITestOutputHelper output)
{
    /// <summary>
    ///     Resolves the webhook endpoint URL from local.settings.json (Functions project),
    ///     falling back to environment variable.
    /// </summary>
    private static string ResolveEndpointUrl()
    {
        // Read directly from Functions local.settings.json first (contains real SAS-signed URL)
        string repoRoot = ResolveRepositoryRoot();
        if (!string.IsNullOrEmpty(repoRoot))
        {
            string localSettingsPath =
                Path.Combine(repoRoot, "src", "ArnBulletinGenerator.Functions", "local.settings.json");
            if (File.Exists(localSettingsPath))
            {
                var json = JsonDocument.Parse(File.ReadAllText(localSettingsPath));
                if (json.RootElement.TryGetProperty("Values", out var values) &&
                    values.TryGetProperty("Webhook__EndpointUrl", out var endpoint))
                {
                    string? url = endpoint.GetString();
                    if (!string.IsNullOrWhiteSpace(url) && !url.Contains("placeholder"))
                        return url;
                }
            }
        }

        // Fallback: check environment variable
        string? envUrl = Environment.GetEnvironmentVariable("Webhook__EndpointUrl");
        if (!string.IsNullOrWhiteSpace(envUrl))
            return envUrl;

        return string.Empty;
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArnBulletinGenerator.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? string.Empty;
    }

    [Fact(Skip = "Requires live webhook endpoint")]
    public async Task SendJobNotificationAsync_WithRealEndpoint_ReturnsSuccessAndPostsToTeams()
    {
        // Arrange — resolve real endpoint URL
        string endpointUrl = ResolveEndpointUrl();
        output.WriteLine($"Resolved endpoint URL: {endpointUrl}");
        endpointUrl.ShouldNotBeNullOrWhiteSpace(
            "Webhook endpoint URL must be configured in local.settings.json or environment");

        var options = Options.Create(new WebhookOptions
        {
            Enabled = true,
            EndpointUrl = endpointUrl,
            OpsConsoleBaseUrl = "http://localhost:5173",
            TimeoutSeconds = 30
        });

        using var httpClient = new HttpClient();
        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new XunitLoggerProvider(output)));
        var logger = loggerFactory.CreateLogger<WebhookNotificationService>();

        var sut = new WebhookNotificationService(options, httpClient, logger);

        // Create a realistic test job
        var job = Job.Create(
            $"integration-test-{Guid.NewGuid():N}"[..32],
            "2GB",
            "Sydney",
            45.0,
            DateTime.UtcNow).Value;

        job.TransitionTo(JobStatus.HereFetched, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.IncidentsSelected, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.ScriptReady, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.AudioReady, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);

        job.ScriptText =
            "Good morning Sydney, here's your traffic update. The M5 is slow through Beverly Hills after an earlier breakdown. Allow an extra ten minutes if you're heading west. The Harbour Bridge is flowing well in both directions. And on the M2, minor delays near the Lane Cove Tunnel. That's traffic, stay safe on the roads.";
        job.MeasuredDurationSec = 42.3;
        job.SetAttemptCount(1);
        job.ScheduledForUtc = DateTime.UtcNow.AddMinutes(15);
        job.SetIncidents(
        [
            new JobIncident("M5-Beverly-Hills", 1, "Breakdown cleared — residual delays", "M5 Motorway, Beverly Hills"),
            new JobIncident("M2-Lane-Cove", 3, "Minor delays", "M2 Motorway near Lane Cove Tunnel")
        ]);

        // Act
        var result = await sut.SendJobNotificationAsync(job, null, CancellationToken.None);

        // Assert
        output.WriteLine($"Result: IsSuccess={result.IsSuccess}, Error={result.Error}");
        result.IsSuccess.ShouldBeTrue($"Expected HTTP 2xx from webhook but got failure: {result.Error}");
    }

    [Fact(Skip = "Requires live webhook endpoint")]
    public async Task SendAlertNotificationAsync_WithRealEndpoint_ReturnsSuccess()
    {
        // Arrange
        string endpointUrl = ResolveEndpointUrl();
        endpointUrl.ShouldNotBeNullOrWhiteSpace("Webhook endpoint URL must be configured");

        var options = Options.Create(new WebhookOptions
        {
            Enabled = true,
            EndpointUrl = endpointUrl,
            OpsConsoleBaseUrl = "http://localhost:5173",
            TimeoutSeconds = 30
        });

        using var httpClient = new HttpClient();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<WebhookNotificationService>();

        var sut = new WebhookNotificationService(options, httpClient, logger);

        // Act
        var result = await sut.SendAlertNotificationAsync(
            "Sydney",
            "2GB",
            "[Integration Test] This is a test alert from the .NET integration test suite.",
            AlertSeverity.Warning,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue($"Expected HTTP 2xx from webhook but got failure: {result.Error}");
    }

    /// <summary>
    ///     Test A: Send the EXACT same simple payload that worked via curl, but using .NET HttpClient.
    ///     If this fails → issue is HttpClient headers/encoding.
    ///     If this succeeds → issue is in the complex payload content.
    /// </summary>
    [Fact(Skip = "Requires live webhook endpoint")]
    public async Task IsolationTestA_SimpleCurlPayload_ViaDotnet()
    {
        string endpointUrl = ResolveEndpointUrl();
        output.WriteLine($"Endpoint: {endpointUrl}");
        endpointUrl.ShouldNotBeNullOrWhiteSpace();

        using var httpClient = new HttpClient();

        // EXACT same payload that worked via curl (empty-act-001)
        var payload = new
        {
            title = "📡 Dotnet Simple Test",
            jobId = "dotnet-simple-001",
            market = "sydney",
            station = "sydney",
            scheduledAtDisplay = "Wed 12 Feb",
            status = "Published",
            previewText = "Simple test sent from .NET HttpClient",
            createdBy = "Bot",
            createdAt = "2026-02-12T03:00:00Z",
            schemaVersion = "2.0",
            correlationId = "dotnet-simple-001",
            allowedActions = new[] { "Approve" },
            url = "http://localhost:5173/jobs/test"
        };

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Dump the exact JSON
        string json = JsonSerializer.Serialize(payload, jsonOptions);
        output.WriteLine($"\n=== PAYLOAD JSON ===\n{json}\n");

        // Send
        var response = await httpClient.PostAsJsonAsync(endpointUrl, payload, jsonOptions);
        output.WriteLine($"HTTP Status: {response.StatusCode}");
        string body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Response: {body}");

        // Also dump request headers for comparison
        var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        request.Content = JsonContent.Create(payload, null, jsonOptions);
        output.WriteLine($"\nContent-Type: {request.Content.Headers.ContentType}");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    ///     Test B: Send using raw StringContent with exact Content-Type "application/json" (no charset).
    ///     This mimics curl's exact behavior.
    /// </summary>
    [Fact(Skip = "Requires live webhook endpoint")]
    public async Task IsolationTestB_RawStringContent_NoChatset()
    {
        string endpointUrl = ResolveEndpointUrl();
        output.WriteLine($"Endpoint: {endpointUrl}");
        endpointUrl.ShouldNotBeNullOrWhiteSpace();

        using var httpClient = new HttpClient();

        // Same simple payload as a raw JSON string
        string json =
            """{"title":"📡 Dotnet Raw Test","jobId":"dotnet - raw - 001","market":"sydney","station":"sydney","scheduledAtDisplay":"Wed 12 Feb","status":"Published","previewText":"Raw string test from.NET","createdBy":"Bot","createdAt":"2026 - 02 - 12T03: 00:00Z","schemaVersion":"2.0","correlationId":"dotnet - raw - 001","allowedActions":["Approve"],"url":"http://localhost:5173/jobs/test"}""";

        output.WriteLine($"\n=== RAW JSON ===\n{json}\n");

        // Use StringContent with explicit "application/json" (no charset) — same as curl
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        // Override to remove charset (curl doesn't send it)
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        output.WriteLine($"Content-Type: {content.Headers.ContentType}");

        var response = await httpClient.PostAsync(endpointUrl, content);
        output.WriteLine($"HTTP Status: {response.StatusCode}");
        string body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Response: {body}");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    ///     Test C: Send the complex payload (matching what the service sends) but as raw string.
    ///     This isolates payload content from HttpClient behavior.
    /// </summary>
    [Fact(Skip = "Requires live webhook endpoint")]
    public async Task IsolationTestC_ComplexPayload_RawString()
    {
        string endpointUrl = ResolveEndpointUrl();
        output.WriteLine($"Endpoint: {endpointUrl}");
        endpointUrl.ShouldNotBeNullOrWhiteSpace();

        using var httpClient = new HttpClient();

        // Complex payload matching what the service builds (with newlines, bullets, etc)
        string json =
            """{"title":"📡 Bulletin Published","jobId":"dotnet - complex - 001","market":"Sydney","station":"2GB","scheduledAtDisplay":"Thu 12 Feb 2026 14:10 UTC","status":"Published","previewText":"Duration: 28.5s / 30s target • 3 traffic incidents\nGood morning Sydney.The M5 has delays through Beverly Hills.","createdBy":"ArnBulletinGenerator","createdAt":"2026 - 02 - 12T03: 08:00.0000000Z","schemaVersion":"2.0","correlationId":"dotnet - complex - 001","allowedActions":[],"url":"http://localhost:5173/jobs/dotnet-complex-001"}""";

        output.WriteLine($"\n=== RAW JSON ===\n{json}\n");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await httpClient.PostAsync(endpointUrl, content);
        output.WriteLine($"HTTP Status: {response.StatusCode}");
        string body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Response: {body}");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }
}

file class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(output, categoryName);
    }

    public void Dispose()
    {
    }
}

file class XunitLogger(ITestOutputHelper output, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{logLevel}] {category}: {formatter(state, exception)}");
    }
}
