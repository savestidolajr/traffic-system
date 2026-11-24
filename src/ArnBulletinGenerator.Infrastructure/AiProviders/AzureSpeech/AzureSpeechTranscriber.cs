using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureSpeech;

/// <summary>
///     Azure AI Speech fast transcription (api-version 2024-11-15) against the existing
///     AIServices account — in-region (whisper is not deployable in australiaeast), native
///     en-AU, and authenticated with the account key already in Key Vault (falling back to
///     the managed identity's Cognitive Services User role). Synchronous endpoint: a 30s
///     bulletin transcribes in a few seconds, cheap enough to sit inline before
///     WaitingApproval.
/// </summary>
public sealed class AzureSpeechTranscriber(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureAIOptions> options,
    TokenCredential credential,
    ILogger<AzureSpeechTranscriber> logger) : IAudioTranscriber
{
    private const string ApiVersion = "2024-11-15";
    private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly AzureAIOptions _options = options.Value;

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.SpeechEndpoint);

    public async Task<Result<string>> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return Result<string>.Failure("Audio transcription is not configured (AiSettings:AzureAI:SpeechEndpoint is empty).");

        try
        {
            string url = $"{_options.SpeechEndpoint!.TrimEnd('/')}/speechtotext/transcriptions:transcribe?api-version={ApiVersion}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);
            }
            else
            {
                var token = await credential.GetTokenAsync(new TokenRequestContext(TokenScopes), cancellationToken);
                request.Headers.Add("Authorization", $"Bearer {token.Token}");
            }

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavAudio);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "audio", "bulletin.wav");
            content.Add(new StringContent("""{"locales":["en-AU"]}"""), "definition");
            request.Content = content;

            var client = httpClientFactory.CreateClient(nameof(AzureSpeechTranscriber));
            using var response = await client.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result<string>.Failure($"Fast transcription returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("combinedPhrases", out var phrases)
                && phrases.ValueKind == JsonValueKind.Array
                && phrases.GetArrayLength() > 0
                && phrases[0].TryGetProperty("text", out var text))
                return Result<string>.Success(text.GetString() ?? string.Empty);

            return Result<string>.Failure("Fast transcription response carried no combinedPhrases text.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AudioPreReview] Transcription failed");
            return Result<string>.Failure($"Transcription failed: {ex.Message}");
        }
    }
}
