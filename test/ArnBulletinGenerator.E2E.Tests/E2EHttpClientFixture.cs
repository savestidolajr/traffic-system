using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ArnBulletinGenerator.E2E.Tests;

public sealed class E2EHttpClientFixture : IAsyncLifetime, IDisposable
{
    public const string DevAdminPrincipal =
        "eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6Im5hbWUiLCJ2YWwiOiJMb2NhbCBEZXYifSx7InR5cCI6InJvbGVzIiwidmFsIjoiQWRtaW4ifSx7InR5cCI6Imh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vaWRlbnRpdHkvY2xhaW1zL29iamVjdGlkZW50aWZpZXIiLCJ2YWwiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifV19";

    public const string DevOperatorPrincipal =
        "eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6Im5hbWUiLCJ2YWwiOiJMb2NhbCBPcGVyYXRvciJ9LHsidHlwIjoicm9sZXMiLCJ2YWwiOiJPcGVyYXRvciJ9LHsidHlwIjoiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9pZGVudGl0eS9jbGFpbXMvb2JqZWN0aWRlbnRpZmllciIsInZhbCI6IjAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLTAwMDAwMDAwMDAwMSJ9XX0=";

    public HttpClient Client { get; } = CreateClient(DevAdminPrincipal);

    public HttpClient AnonymousClient { get; } = CreateClient(null);

    public HttpClient OperatorClient { get; } = CreateClient(DevOperatorPrincipal);

    public async Task InitializeAsync() => await WaitForHealthAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        Client.Dispose();
        AnonymousClient.Dispose();
        OperatorClient.Dispose();
    }

    public static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, E2EJson.Options), Encoding.UTF8, "application/json");

    public async Task WaitForHealthAsync()
    {
        HttpStatusCode? lastStatus = null;
        await Eventually.UntilAsync(async () =>
        {
            try
            {
                using var response = await AnonymousClient.GetAsync("health");
                lastStatus = response.StatusCode;
                return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(2),
            $"Functions host did not become ready. Last status: {lastStatus?.ToString() ?? "no response"}");
    }

    private static HttpClient CreateClient(string? principal)
    {
        var configured = Environment.GetEnvironmentVariable("ARN_E2E_FUNCTIONS_BASE_URL") ?? "http://127.0.0.1:7071/api";
        var baseUrl = configured.TrimEnd('/') + "/";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(principal))
            client.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL", principal);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class BackendE2ECollection : ICollectionFixture<E2EHttpClientFixture>
{
    public const string Name = "Backend E2E";
}
