using ArnBulletinGenerator.Domain.Contracts;

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for the entire script AI pipeline (generation, review,
///     sentence patch, safety, direction, rewrite): returns a fixed traffic script through every
///     stage instead of calling Azure OpenAI.
/// </summary>
public sealed class FakeScriptAiProvider(FakeExternalCallLog log) :
    IScriptGenerator,
    IScriptReviewer,
    IScriptRefiner,
    IScriptSafetyReviewer,
    IScriptDirector,
    IDeliveryWriter,
    IScriptRewriter
{
    // Sized to a real 30s slot (~78 words ≈ 31s at the 150 wpm FakeAudioSynthesizer reads at), so the
    // two fakes agree with each other. It used to be 33 words — 13s of speech — and only ever filled
    // a 30s slot because FakeAudioSynthesizer claimed a flat 30s for any input. Once that fake became
    // length-proportional the mismatch surfaced as "could not fill the 30s slot (best spoken 16.4s)"
    // and every queue-driven E2E test fell through to fallback. The first two sentences are kept
    // verbatim: E2E assertions reference these road names.
    private const string BaseScript =
        "Heavy delays are building on the M1 Pacific Motorway after a crash near Smith Street Motorway. " +
        "Smith Street Motorway is slower around roadworks, and Gold Coast Highway has a lane restriction through Broadbeach. " +
        "Bermuda Street is heavy northbound between Robina and Nerang, and there's a breakdown on Southport Nerang Road. " +
        "The Gooding Drive on-ramp is busy, while Nerang Broadbeach Road runs well in both directions. " +
        "Partly cloudy, 22 degrees. Unleaded's cheapest at a dollar 74 in Southport.";

    public Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken = default) =>
        GenerateScriptAsync(context, 0, VoiceSettings.Default, null, cancellationToken);

    public Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        double syllablesPerSecond,
        VoiceSettings settings,
        CancellationToken cancellationToken = default) =>
        GenerateScriptAsync(context, syllablesPerSecond, settings, null, cancellationToken);

    public Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        double syllablesPerSecond,
        VoiceSettings settings,
        string? promptTemplate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"script generate job={context.JobId} duration={context.TargetDurationSeconds:F0}");
        return Task.FromResult(Result<DraftedScript>.Success(new DraftedScript(BaseScript, [])));
    }

    public Task<Result<ScriptReviewResult>> ReviewScriptAsync(
        string script,
        int targetDurationSeconds,
        int? targetWordCountOverride = null,
        string? rejectionFeedback = null,
        BroadcastWithTraffic? context = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DraftedSentence>? draftedSentences = null,
        IReadOnlyList<string>? removedSentences = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"script review chars={script.Length}");
        return Task.FromResult(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));
    }

    public Task<Result<DraftedSentence>> PatchSentenceAsync(
        BroadcastWithTraffic context,
        DraftedSentence sentence,
        string instruction,
        IReadOnlyList<EvidenceUnit> allowedEvidence,
        int wordBudget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"sentence patch job={context.JobId} order={sentence.Order} instruction={instruction.Length}");
        return Task.FromResult(Result<DraftedSentence>.Success(sentence));
    }

    public Task<Result<ScriptSafetyResult>> ReviewScriptSafetyAsync(
        string script,
        string? jobId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"script safety chars={script.Length}");
        if (script.Contains("blocked-test-word", StringComparison.Ordinal))
        {
            var result = new ScriptSafetyResult(false, ["blocked-test-word: deterministic fake safety failure"]);
            return Task.FromResult(Result<ScriptSafetyResult>.Success(result));
        }

        return Task.FromResult(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
    }

    public Task<Result<DeliveryRewrite>> RewriteAsync(
        string approvedScript,
        string operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add("script rewrite-for-register");
        // Deterministic stand-in: prepend a manner tag and mark one word, so the offline UI and the
        // e2e tests exercise the whole loop without a model call. Words are otherwise untouched.
        var rewritten = $"[quietly] {approvedScript}";
        int words = WordCounter.Count(DeliveryTags.Strip(rewritten));
        return Task.FromResult(Result<DeliveryRewrite>.Success(
            new DeliveryRewrite(rewritten, words, words, DeliveryTags.Extract(rewritten), [])));
    }

    public Task<Result<ScriptRewrite>> RewriteAsync(
        string currentScript,
        string operatorInstruction,
        BroadcastWithTraffic context,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add("script rewrite-content");
        // Deterministic stand-in: reword the lead sentence so the content-rewrite loop (preview,
        // diff, apply) is exercisable offline. The words change, so the UI can show a real diff.
        string plain = DeliveryTags.Strip(currentScript);
        string rewritten = string.IsNullOrWhiteSpace(plain)
            ? plain
            : $"Reworked to follow the instruction. {plain}";
        int before = WordCounter.Count(plain);
        int after = WordCounter.Count(rewritten);
        return Task.FromResult(Result<ScriptRewrite>.Success(
            new ScriptRewrite(rewritten, before, after)));
    }

    public Task<Result<string>> DirectAsync(
        string plainScript,
        DeliveryStylePolicy policy,
        string? operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"script direct neutral={policy.IsNeutral}");
        if (policy.IsNeutral || !string.Equals(ttsModel, "eleven_v3", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Result<string>.Success(plainScript));

        var tag = policy.AllowedTags.FirstOrDefault(t => DeliveryTags.HasTags(t)) ?? "[calm]";
        var sentences = plainScript.Split(". ", 2, StringSplitOptions.None);
        var tagged = sentences.Length == 1
            ? $"{tag} {plainScript}"
            : $"{tag} {sentences[0]}. {sentences[1]}";

        return Task.FromResult(Result<string>.Success(
            DeliveryTags.WordsPreserved(plainScript, tagged) ? tagged : plainScript));
    }
}
