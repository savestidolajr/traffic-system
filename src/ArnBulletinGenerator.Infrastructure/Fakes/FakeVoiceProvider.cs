using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for the ElevenLabs voice catalog: returns two fixed voices,
///     one Voice-Library-licensed and remix-blocked and one remixable, mirroring production
///     remix eligibility rules.
///     <para>
///         The voice IDS are deliberately real: <c>Broadcast:DefaultVoiceId</c> is
///         <c>oKDz3nEzNfwKIVvUwPs0</c>, so the preview and calibration paths only resolve under
///         Fake mode if this catalogue answers to it. The NAMES are deliberately fake, and must
///         stay that way. They previously read "ARN Hudson" and "ARN Roger" — names ElevenLabs
///         has never used for these ids — which made the fake catalogue look like a record of
///         the real account and let a config fiction survive six weeks of green E2E runs
///         (see BroadcastOptions.DefaultVoiceId). A fake must be obviously a fake.
///     </para>
/// </summary>
public sealed class FakeVoiceProvider(FakeExternalCallLog log) : IVoiceProvider
{
    public Task<Result<IReadOnlyList<Voice>>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add("voices list");
        IReadOnlyList<Voice> voices =
        [
            // Mirrors production: a Voice Library PVC copied with a finite notice period —
            // ElevenLabs rejects remix on these regardless of notice-period length.
            new Voice("oKDz3nEzNfwKIVvUwPs0", "Fake AU Male VO", "professional", "Deterministic fake ElevenLabs voice",
                new Dictionary<string, string> { ["provider"] = "ElevenLabs" })
            { ModelIds = ["eleven_v3", "eleven_multilingual_v2"], CanRemix = false },
            // Owned outright (no Voice Library sharing record) → remixable.
            new Voice("CwhRBWXzGAHq8TQ4Fs17", "Fake AU Male Casual", "professional", "Deterministic fake ElevenLabs voice",
                new Dictionary<string, string> { ["provider"] = "ElevenLabs" })
            { ModelIds = ["eleven_v3", "eleven_multilingual_v2"], CanRemix = true }
        ];
        return Task.FromResult(Result<IReadOnlyList<Voice>>.Success(voices));
    }
}
