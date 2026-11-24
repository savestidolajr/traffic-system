using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for the ElevenLabs pronunciation dictionary API: derives
///     stable fake dictionary/version ids from a SHA-256 hash of the rule set instead of calling
///     ElevenLabs.
/// </summary>
public sealed class FakePronunciationDictionaryProvider(FakeExternalCallLog log) : IPronunciationDictionaryProvider
{
    public Task<Result<PronunciationLocator>> CreateAsync(
        string name,
        IReadOnlyList<PronunciationRule> rules,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var suffix = StableSuffix(name, string.Join(';', rules.Select(r => r.Signature)));
        var locator = new PronunciationLocator($"fake-dict-{suffix}", $"fake-dict-version-{suffix}-1");
        log.Add($"pronunciation create id={locator.DictionaryId}");
        return Task.FromResult(Result<PronunciationLocator>.Success(locator));
    }

    public Task<Result<string>> AddRulesAsync(
        string dictionaryId,
        IReadOnlyList<PronunciationRule> rules,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = $"fake-dict-version-{StableSuffix(dictionaryId, string.Join(';', rules.Select(r => r.Signature)))}";
        log.Add($"pronunciation add id={dictionaryId}");
        return Task.FromResult(Result<string>.Success(version));
    }

    public Task<Result<string>> RemoveRulesAsync(
        string dictionaryId,
        IReadOnlyList<string> ruleStrings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = $"fake-dict-version-{StableSuffix(dictionaryId, string.Join(';', ruleStrings.OrderBy(x => x, StringComparer.Ordinal)))}";
        log.Add($"pronunciation remove id={dictionaryId}");
        return Task.FromResult(Result<string>.Success(version));
    }

    public Task<Result<string>> SetRulesAsync(
        string dictionaryId,
        IReadOnlyList<PronunciationRule> rules,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = $"fake-dict-version-{StableSuffix(dictionaryId, string.Join(';', rules.Select(r => r.Signature)))}";
        log.Add($"pronunciation set-rules id={dictionaryId}");
        return Task.FromResult(Result<string>.Success(version));
    }

    public Task<Result> ArchiveAsync(string dictionaryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"pronunciation archive id={dictionaryId}");
        return Task.FromResult(Result.Success());
    }

    private static string StableSuffix(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexString(bytes[..4]).ToLowerInvariant();
    }
}
