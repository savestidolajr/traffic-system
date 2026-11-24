using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Simple URL provider for local development that returns a dummy URL.
///     Prevents authentication errors with SAS generation in local environments.
/// </summary>
public sealed class LocalAudioUrlProvider : IAudioUrlProvider
{
    public Task<Result<string>> GetAudioUrlAsync(string audioFileName, CancellationToken cancellationToken = default)
    {
        // Return a local-friendly dummy URL or just the filename
        // In local console mode, we don't actually need a working URL for the frontend 
        // if we're just testing the orchestration flow.
        return Task.FromResult(Result<string>.Success($"local://{audioFileName}"));
    }

    public Option<string> ResolveOwnBlobName(string audioUrl)
    {
        if (string.IsNullOrEmpty(audioUrl))
            return Option<string>.None;

        // Local console mode has no blob container: treat absolute http(s) URLs as external,
        // everything else as a locally-owned file name.
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
            return Option<string>.None;

        return Option<string>.Some(audioUrl);
    }

    public Task<Result<AudioStreamHandle>> OpenAudioStreamAsync(
        string audioUrl, AudioByteRange? range, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<AudioStreamHandle>.Failure(
            "Audio streaming is not supported in local console mode"));
}
