using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Manages pronunciation dictionaries in the TTS provider (ElevenLabs). Distinct from
///     attaching a dictionary at synthesis time: this is the write side that creates a
///     dictionary and edits its rules. Every mutation mints a NEW provider version id, which the
///     caller persists so the right version is attached on air.
/// </summary>
public interface IPronunciationDictionaryProvider
{
    /// <summary>
    ///     Creates a new provider dictionary seeded with <paramref name="rules" />.
    ///     Returns the dictionary id and the initial version id.
    /// </summary>
    Task<Result<PronunciationLocator>> CreateAsync(
        string name, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds/replaces rules (a rule with the same word-to-replace is overwritten).
    ///     Returns the new version id.
    /// </summary>
    Task<Result<string>> AddRulesAsync(
        string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes rules by the words they replace. Returns the new version id.
    /// </summary>
    Task<Result<string>> RemoveRulesAsync(
        string dictionaryId, IReadOnlyList<string> ruleStrings, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces ALL rules on an existing dictionary with the supplied set, returning the new
    ///     version id. Used to maintain the alias-only version line a phoneme-carrying dictionary
    ///     needs on models (eleven_multilingual_v2) that would delete phoneme-matched words:
    ///     the same upstream dictionary, at a version holding only its alias rules.
    /// </summary>
    Task<Result<string>> SetRulesAsync(
        string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Takes a provider dictionary out of active use. ElevenLabs has NO hard delete for
    ///     pronunciation dictionaries — archiving (PATCH <c>archived=true</c>) is the closest
    ///     removal: the dictionary stops appearing as active but stays recoverable. Used to roll
    ///     back an upstream create whose local record could not be persisted, so a failed save
    ///     cannot leak an unreferenced live dictionary. Idempotent: a dictionary that is already
    ///     gone (404) is treated as success.
    /// </summary>
    Task<Result> ArchiveAsync(
        string dictionaryId, CancellationToken cancellationToken = default);
}
