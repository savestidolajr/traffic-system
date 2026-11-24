using System.Text;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Owns delivery direction (ElevenLabs v3 audio tags): applying the resolved delivery policy to
///     a validated script without ever changing its spoken words. Extracted from
///     <see cref="ScriptGenerationEngine" /> — see that class for the fields it did not need.
/// </summary>
internal sealed class DeliveryDirector(
    IScriptDirector scriptDirector,
    IOptions<DeliveryOptions> deliveryOptions,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater)
{
    private readonly DeliveryOptions _deliveryOptions = deliveryOptions.Value;

    /// <summary>
    ///     Applies the resolved delivery policy (ElevenLabs v3 audio tags) to a script, returning the
    ///     tagged synthesis text. The director adds tags only (word-immutable) and fails open to the
    ///     plain script — a neutral policy, a no-op direction, or a tagged-text safety block all
    ///     return the input unchanged.
    /// </summary>
    internal async Task<string> ApplyDeliveryDirectionAsync(BroadcastWithTraffic context, string script,
        CancellationToken cancellationToken)
    {
        var deliveryPolicy = context.DeliveryStyle.GetValueOr(_deliveryOptions.DefaultPolicy);
        string? intent = context.DeliveryIntent.Match<string?>(v => v, () => null);
        // An explicit operator re-direction ("read it calmer") runs the director even when the
        // station's style is neutral, using a permissive operator-directed palette.
        if (deliveryPolicy.IsNeutral && !string.IsNullOrWhiteSpace(intent))
            deliveryPolicy = DeliveryStylePolicy.OperatorDirected;
        string? ttsModel = context.Model.Match<string?>(v => v, () => null);
        // A neutral style directs nothing - but the script it returns may still carry tags an
        // operator typed or a previous v3 read left behind, and on a non-v3 voice those would be
        // SPOKEN. Strip on the way past: this is the one exit that skips the director's own gate.
        if (deliveryPolicy.IsNeutral)
        {
            string safe = DeliveryTags.ForModel(script, ttsModel);
            if (!ReferenceEquals(safe, script))
                logger.LogWarning(
                    "[Director] Job {JobId} script carried tag(s) with a neutral style on non-v3 model " +
                    "'{Model}'; stripped before synthesis.", context.JobId, ttsModel ?? "(none)");
            return safe;
        }

        var directed = await scriptDirector.DirectAsync(script, deliveryPolicy, intent, ttsModel, context.JobId, cancellationToken);
        if (directed.IsSuccess && !string.IsNullOrWhiteSpace(directed.Value) && directed.Value != script)
        {
            // The director only ADDS audio tags — the spoken words are immutable. Verify that
            // invariant deterministically (stripping the tags reproduces the approved words)
            // instead of paying for a second LLM safety review of already-safety-passed words.
            if (!WordsEqual(DeliveryTags.Strip(directed.Value), script))
            {
                logger.LogWarning(
                    "[Director] Job {JobId} tagged script altered the spoken words; using plain script.",
                    context.JobId);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "Delivery tags reverted: the tagged read changed the words.", cancellationToken);
                return script;
            }

            // The word guard above is case-INSENSITIVE by design (see WordsEqual), so the director
            // can return "moving NICELY" and pass. That was tolerable when capitals were the only
            // emphasis mechanism available, but on ElevenLabs v3 emphasis belongs in an audio TAG,
            // and ALL-CAPS reaching the synthesiser risks a shouted or letter-by-letter read.
            // Observed live 2026-08-20: cairns job 6ea0155c aired "both moving NICELY this morning!"
            // and sydney 90bac081 produced "SUNNY". Rather than reject the whole tagged read and
            // lose good tags (the regression behind job 32a5e344), keep the tags and restore the
            // APPROVED casing word-for-word. The spoken words are then byte-identical to the text
            // that passed review and safety, which is what the guard's contract already claims.
            string tagged = RestoreApprovedCasing(directed.Value, script);

            // Punctuation is exempt from the word guard on purpose (see WordsEqual), but "add an
            // ellipsis for weight" and "add a sentence" are the same edit to that guard, and they
            // are not the same thing to anything downstream. ScriptSentenceSplitter — the canonical
            // splitter BulletinDurationFitter drops whole sentences by, and the form persisted as
            // ScriptText — breaks on '…' before a capital. Dev goldcoast de2d1b60 (2026-09-03): the
            // composer joined a closure and its condition with a semicolon, every gate passed that
            // as ONE sentence, and the director returned "…over about half a kay… Northbound
            // traffic cannot use the closed section." The aired read carried an orphan sentence
            // naming no road, created after the last gate had run and with no LLM call in the job
            // record to explain it. So a directed read may re-punctuate freely INSIDE its
            // sentences and may not change how many there are.
            if (SentenceCountOf(tagged) != SentenceCountOf(script))
            {
                logger.LogWarning(
                    "[Director] Job {JobId} tagged script changed the sentence structure " +
                    "({Approved} -> {Tagged}); using plain script.",
                    context.JobId, SentenceCountOf(script), SentenceCountOf(tagged));
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "Delivery tags reverted: the tagged read split or joined a sentence.",
                    cancellationToken);
                return script;
            }

            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Delivery style applied: {DeliveryTags.Extract(tagged).Count} audio tag(s).",
                cancellationToken);
            return tagged;
        }

        // A style was requested but the director produced no usable tags (LLM kept altering words, or
        // returned the script unchanged). Fail open to a neutral read; surface it on the job.
        await jobStatusUpdater.LogJobAsync(context.JobId,
            "Delivery style requested but no audio tags were applied this time — aired with a neutral read.",
            cancellationToken);
        return script;
    }

    // Word-sequence equality over letters and digits only, case-insensitive — used to confirm the
    // delivery director changed no spoken word. Punctuation is deliberately NOT compared:
    // director.md rule 3 explicitly permits capitalising a word for emphasis and adding ellipses or
    // an exclamation mark for weight, so a raw whitespace-token comparison rejected exactly the
    // reads the prompt asked for — one added "!" threw away a correctly tagged bulletin and aired it
    // flat (dev job 32a5e344, 2026-07-31). Digits are still tokenised, so "$1.83" -> "$183" and any
    // other number mangling remains a mismatch.
    private static bool WordsEqual(string a, string b)
    {
        var wa = WordTokenPattern.Matches(a);
        var wb = WordTokenPattern.Matches(b);
        if (wa.Count != wb.Count)
            return false;
        for (int i = 0; i < wa.Count; i++)
            if (!string.Equals(wa[i].Value, wb[i].Value, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    // Sentences as the rest of the platform counts them. Tags are stripped first: the canonical
    // splitter's boundary lookahead admits '[', so an un-stripped "[urgent]" after a full stop
    // would read as a sentence of its own and this guard would reject every tagged read.
    private static int SentenceCountOf(string script) =>
        ScriptSentenceSplitter.Split(DeliveryTags.Strip(script)).Count;


    /// <summary>
    ///     Rewrites <paramref name="tagged" /> so every SPOKEN word carries the casing it had in the
    ///     <paramref name="approved" /> script, leaving audio tags, punctuation and whitespace exactly
    ///     where the director put them.
    /// </summary>
    /// <remarks>
    ///     Words inside an audio tag are deliberately skipped. A tag like <c>[urgent]</c> contains a
    ///     word token that has no counterpart in the approved script, so counting it would make the
    ///     two sequences differ in length and abandon the restore — which is exactly what happened
    ///     before this guard existed: the tagged read was thrown away and the bulletin aired flat
    ///     with no delivery tags at all. Tag CONTENTS are never spoken, so they must not participate.
    /// </remarks>
    private static string RestoreApprovedCasing(string tagged, string approved)
    {
        var tagSpans = DeliveryTagPattern.Matches(tagged);
        bool InsideTag(int index) =>
            tagSpans.Any(span => index >= span.Index && index < span.Index + span.Length);

        var spokenWords = WordTokenPattern.Matches(tagged).Where(m => !InsideTag(m.Index)).ToList();
        var approvedWords = WordTokenPattern.Matches(approved);
        if (spokenWords.Count != approvedWords.Count)
            return tagged;

        var sb = new StringBuilder(tagged.Length);
        int cursor = 0;
        for (int i = 0; i < spokenWords.Count; i++)
        {
            var token = spokenWords[i];
            sb.Append(tagged, cursor, token.Index - cursor);
            sb.Append(approvedWords[i].Value);
            cursor = token.Index + token.Length;
        }

        sb.Append(tagged, cursor, tagged.Length - cursor);
        return sb.ToString();
    }

    private static readonly Regex DeliveryTagPattern =
        new(@"\[[^\]]*\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordTokenPattern =
        new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
