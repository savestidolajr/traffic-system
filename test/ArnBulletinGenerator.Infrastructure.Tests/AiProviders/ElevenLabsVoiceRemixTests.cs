using System.Net;
using System.Text;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

/// <summary>
///     The wire contract for the remix-voice management endpoints (mint + the VC-3 rollback
///     delete). The delete is the orphan-cleanup half of remix adoption: it must hit the voice
///     endpoint, treat an already-gone voice (404) as success so a retried rollback cannot fail,
///     and surface real HTTP errors so the caller can log the orphan loudly.
/// </summary>
public sealed class ElevenLabsVoiceRemixTests
{
    private const string TestApiKey = "test-api-key";
    private const string TestVoiceId = "21m00Tcm4TlvDq8ikWAM";

    private static ElevenLabsOptions Options() => new()
    {
        ApiKey = TestApiKey,
        BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
        ModelId = "eleven_v3",
        OutputFormat = "mp3_44100_192",
        UseTimestampsEndpoint = false
    };

    private static ElevenLabsClient Client(ElevenLabsOptions opts, FakeHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<ElevenLabsClient>.Instance);

    [Fact]
    public async Task DeleteVoiceAsync_DeletesTheVoiceEndpoint_Succeeds()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, content: """{"status":"ok"}""");
        var client = Client(Options(), handler);

        var result = await client.DeleteVoiceAsync(TestVoiceId);

        result.IsSuccess.ShouldBeTrue();
        handler.LastMethod.ShouldBe(HttpMethod.Delete);
        handler.LastUri!.ShouldContain($"/voices/{TestVoiceId}");
    }

    [Fact]
    public async Task DeleteVoiceAsync_NotFound_TreatedAsSuccess()
    {
        // Idempotent rollback: a voice that is already gone has nothing left to delete.
        var handler = new FakeHandler(HttpStatusCode.NotFound);
        var client = Client(Options(), handler);

        var result = await client.DeleteVoiceAsync(TestVoiceId);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteVoiceAsync_HttpError_SurfacesFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.UnprocessableEntity, content: """{"detail":{"message":"bad"}}""");
        var client = Client(Options(), handler);

        var result = await client.DeleteVoiceAsync(TestVoiceId);

        result.IsFailure.ShouldBeTrue();
    }

    private sealed class FakeHandler(HttpStatusCode status, string content = "")
        : HttpMessageHandler
    {
        public string? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString();
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
