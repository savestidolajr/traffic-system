using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using global::WireMock;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     Base class for WireMock integration tests.
///     Starts a WireMock server on a dynamic port and provides a ServiceProvider
///     configured to point real HTTP clients at the WireMock server.
/// </summary>
public abstract class WireMockTestBase : IAsyncLifetime
{
    private readonly List<Action> _cleanupActions = [];

    private WireMockServer? _server;
    private ServiceProvider? _serviceProvider;

    /// <summary>
    ///     The running WireMock server instance.
    /// </summary>
    protected WireMockServer Server => _server ?? throw new InvalidOperationException("Server not started");

    /// <summary>
    ///     The fully-wired service provider with in-memory repositories and real HTTP clients
    ///     configured to call the WireMock server.
    /// </summary>
    protected ServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Services not built");

    /// <summary>
    ///     Override this to provide additional config key-value pairs.
    ///     The WireMock base URLs are already set — add service-specific overrides here.
    /// </summary>
    protected virtual Dictionary<string, string?> AdditionalConfigOverrides => new();

    public async Task InitializeAsync()
    {
        _server = WireMockServer.Start();

        var configOverrides = new Dictionary<string, string?>
        {
            // HERE Maps — all three services point at WireMock
            ["Here:TrafficBaseUrl"] = _server.Url,
            ["Here:GeocodingBaseUrl"] = _server.Url,
            ["Here:RoutingBaseUrl"] = _server.Url,
            ["Here:BatchReverseGeocodeUrl"] = $"{_server.Url}/multi-revgeocode",
            ["Here:ApiKey"] = "wiremock-test-key",

            // Azure OpenAI
            ["AiSettings:AzureAI:Endpoint"] = _server.Url,
            ["AiSettings:AzureAI:ApiKey"] = "wiremock-test-key",
            ["AiSettings:AzureAI:Model"] = "gpt-5-mini",

            // ElevenLabs
            ["AiSettings:ElevenLabs:BaseUrl"] = $"{_server.Url}/v1/text-to-speech",
            ["AiSettings:ElevenLabs:ApiKey"] = "wiremock-test-key",
            ["AiSettings:ElevenLabs:ModelId"] = "eleven_v3",
            ["AiSettings:ElevenLabs:OutputFormat"] = "pcm_24000",
            ["AiSettings:ElevenLabs:UseTimestampsEndpoint"] = "true",

            // Webhook
            ["Webhook:Enabled"] = "true",
            ["Webhook:EndpointUrl"] = $"{_server.Url}/webhook",
            ["Webhook:TimeoutSeconds"] = "10",
            ["Webhook:MaxRetries"] = "0",

            // Output
            ["Providers:OutputProvider"] = "Console",

            // Storage — use local emulator (not needed for WireMock tests)
            ["Storage:AccountName"] = "UseDevelopmentStorage=true",

            // Prompts
            ["Prompts:SystemPrompt"] = "You are a test traffic presenter for {{city}}.",
            ["Prompts:ReviewPrompt"] = "Review this script.",
            ["Prompts:RefinePrompt"] = "Refine this script."
        };

        // Merge test-specific overrides
        foreach (var kvp in AdditionalConfigOverrides)
            configOverrides[kvp.Key] = kvp.Value;

        var configuration = BuildConfiguration(configOverrides);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);

        // In-memory repositories (no Azure Storage dependency)
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();

        // HITL approval chain is not needed for WireMock E2E tests
        services.AddApplication();

        _serviceProvider = services.BuildServiceProvider();
        await OnInitializedAsync();
    }

    /// <summary>
    ///     Hook for derived classes to register WireMock stubs after the server is ready.
    /// </summary>
    protected virtual Task OnInitializedAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Registers cleanup action to be called during DisposeAsync.
    /// </summary>
    protected void RegisterCleanup(Action cleanup)
    {
        _cleanupActions.Add(cleanup);
    }

    /// <summary>
    ///     The most recent logged request, asserted present. WireMock.Net annotates
    ///     <c>RequestMessage</c> as nullable; tests inspecting requests want a hard failure,
    ///     not a null-flow warning, when nothing was captured.
    /// </summary>
    protected IRequestMessage LastRequest()
    {
        var entry = Server.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        var request = entry.RequestMessage;
        Assert.NotNull(request);
        return request;
    }

    /// <summary>
    ///     Body of the most recent logged request, asserted present.
    /// </summary>
    protected string LastRequestBody()
    {
        var body = LastRequest().Body;
        Assert.NotNull(body);
        return body;
    }

    /// <summary>
    ///     The first logged request whose URL contains <paramref name="urlFragment" />, asserted present.
    /// </summary>
    protected IRequestMessage RequestTo(string urlFragment)
    {
        var entry = Server.LogEntries.FirstOrDefault(e =>
            e.RequestMessage?.Url?.Contains(urlFragment, StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(entry);
        var request = entry.RequestMessage;
        Assert.NotNull(request);
        return request;
    }

    public async Task DisposeAsync()
    {
        foreach (var cleanup in _cleanupActions)
            cleanup();

        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        _server?.Stop();
        _server?.Dispose();
    }

    /// <summary>
    ///     Helper: stub a GET endpoint with a JSON response body.
    /// </summary>
    protected void StubGet(string pathPattern, string jsonResponse, int statusCode = 200)
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(pathPattern)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonResponse));
    }

    /// <summary>
    ///     Helper: stub a POST endpoint with a JSON response body.
    /// </summary>
    protected void StubPost(string pathPattern, string jsonResponse, int statusCode = 200)
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(pathPattern)).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonResponse));
    }

    /// <summary>
    ///     Helper: stub a POST endpoint returning binary data (e.g. audio).
    /// </summary>
    protected void StubPostBinary(string pathPattern, byte[] binaryData, int statusCode = 200)
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(pathPattern)).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "audio/pcm")
                .WithBody(binaryData));
    }

    /// <summary>
    ///     Helper: stub an endpoint to return a specific status code (e.g. 500, 429).
    /// </summary>
    protected void StubError(string method, string pathPattern, int statusCode, string? body = null)
    {
        var request = Request.Create().WithPath(new RegexMatcher(pathPattern));
        request = method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? request.UsingPost()
            : request.UsingGet();

        var response = Response.Create().WithStatusCode(statusCode);
        if (body is not null)
            response = response.WithBody(body);

        Server.Given(request).RespondWith(response);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> overrides)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(overrides!)
            .AddEnvironmentVariables()
            .Build();
    }
}
