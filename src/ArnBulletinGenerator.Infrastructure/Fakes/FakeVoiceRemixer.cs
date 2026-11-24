using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for ElevenLabs voice remixing: derives stable fake
///     preview/voice ids from a SHA-256 hash of the remix inputs instead of calling ElevenLabs.
/// </summary>
public sealed class FakeVoiceRemixer(FakeExternalCallLog log) : IVoiceRemixer
{
    public Task<Result<IReadOnlyList<VoiceRemixPreview>>> RemixVoiceAsync(
        string voiceId,
        string voiceDescription,
        string previewText,
        double? promptStrength,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = $"fake-remix-{StableSuffix(voiceId, voiceDescription, previewText, promptStrength?.ToString("F2") ?? "default")}";
        log.Add($"voice remix id={id}");
        IReadOnlyList<VoiceRemixPreview> previews =
        [
            new(id, GeneratePreviewBytes(), 1.0)
        ];
        return Task.FromResult(Result<IReadOnlyList<VoiceRemixPreview>>.Success(previews));
    }

    public Task<Result<string>> CreateVoiceFromPreviewAsync(
        string generatedVoiceId,
        string voiceName,
        string voiceDescription,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = generatedVoiceId.StartsWith("fake-remix-", StringComparison.Ordinal)
            ? generatedVoiceId
            : $"fake-remix-{StableSuffix(generatedVoiceId, voiceName)}";
        log.Add($"voice remix create id={id}");
        return Task.FromResult(Result<string>.Success(id));
    }

    public Task<Result> DeleteVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"voice remix delete id={voiceId}");
        return Task.FromResult(Result.Success());
    }

    private static string StableSuffix(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexString(bytes[..4]).ToLowerInvariant();
    }

    private static byte[] GeneratePreviewBytes() => [0x52, 0x49, 0x46, 0x46];
}
