using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The kind of pronunciation rule, which decides on which TTS models it takes effect.
///     ElevenLabs applies <see cref="Alias" /> rules on every model. <see cref="Phoneme" />
///     rules are supported ONLY on the turbo/flash v2 family: on <c>eleven_v3</c> they are
///     silently ignored (benign — alias rules still apply), but on
///     <c>eleven_multilingual_v2</c> every word a phoneme rule matches is silently DROPPED
///     from the synthesized audio entirely (verified live 2026-07-19 — a 14-lexeme IPA
///     dictionary deleted every matched road name from a 30s read). Alias is the universal,
///     model-agnostic fix.
/// </summary>
public enum PronunciationRuleType
{
    /// <summary>Word → respelling substitution ("Warringah" → "Warringa"). Applies on ALL models.</summary>
    Alias = 0,

    /// <summary>
    ///     IPA/CMU phonetic rule. Supported on turbo/flash v2 only; ignored by eleven_v3;
    ///     DROPS matched words on eleven_multilingual_v2 (see <see cref="PronunciationRule.PhonemeRulesSafeFor" />).
    /// </summary>
    Phoneme = 1
}

/// <summary>
///     One pronunciation rule in a dictionary. An alias rule respells a word; a phoneme rule
///     pins its phonetics in an alphabet (IPA or CMU). Both are matched against
///     <see cref="StringToReplace" />. Mirrors the ElevenLabs add-rules request shape — the
///     dictionary that owns this rule is round-tripped to ElevenLabs, which applies whichever
///     rule types the synthesis model supports.
/// </summary>
public sealed record PronunciationRule(
    PronunciationRuleType Type,
    string StringToReplace,
    string Replacement,
    string? Alphabet,
    bool CaseSensitive,
    bool WordBoundaries)
{
    /// <summary>Alphabets ElevenLabs accepts for a phoneme rule.</summary>
    public static readonly IReadOnlyList<string> Alphabets = ["ipa", "cmu"];

    /// <summary>
    ///     True when this rule takes effect regardless of TTS model. Phoneme rules are dropped
    ///     by eleven_v3, so the studio surfaces this to warn when a v3 station relies on one.
    /// </summary>
    [JsonIgnore]
    public bool AppliesOnAllModels => Type == PronunciationRuleType.Alias;

    /// <summary>
    ///     Whether phoneme rules are SAFE to send to the given TTS model. Turbo/flash v2 render
    ///     them; eleven_v3 ignores them (benign, alias rules still apply, so attaching is safe);
    ///     <c>eleven_multilingual_v2</c> silently DELETES every matched word from the audio —
    ///     a dictionary containing phoneme rules must NOT be attached to it (verified live
    ///     2026-07-19).
    /// </summary>
    public static bool PhonemeRulesSafeFor(string model) =>
        !model.Contains("multilingual", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Whether THIS rule's type takes effect on the given TTS model. Alias rules apply on
    ///     every model. Phoneme rules apply only on the turbo/flash v2 family: <c>eleven_v3</c>
    ///     skips them (benign) and <c>eleven_multilingual_v2</c> would DELETE matched words, so a
    ///     phoneme rule is never "applied" there. Composes the two authoritative model
    ///     predicates — <see cref="PhonemeRulesSafeFor" /> (safe to attach) and
    ///     <see cref="DeliveryTags.SupportsTags" /> (the v3 family, which interprets inline tags
    ///     and ignores phoneme rules) — rather than a third inline model check, so the preview's
    ///     "applied" answer cannot drift from either.
    /// </summary>
    public bool AppliesOn(string voiceModel) =>
        Type == PronunciationRuleType.Alias
        || (PhonemeRulesSafeFor(voiceModel) && !DeliveryTags.SupportsTags(voiceModel));

    /// <summary>
    ///     True when this rule's word occurs in <paramref name="text" /> under the same
    ///     word-boundary/case semantics the real substitution uses. Reference implementation:
    ///     <c>web/src/features/jobs/lib/pronunciation-matches.ts</c> <c>findRuleMatches</c> — when
    ///     <see cref="WordBoundaries" /> is set the word must stand alone between non
    ///     letter/digit/underscore characters, and matching is case-insensitive unless
    ///     <see cref="CaseSensitive" /> is set. The preview measures its applied counts with this
    ///     so "N rules applied" means the words were actually present in the sample.
    /// </summary>
    public bool MatchesIn(string text)
    {
        if (string.IsNullOrEmpty(StringToReplace) || string.IsNullOrEmpty(text))
            return false;

        string pattern = WordBoundaries
            ? $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(StringToReplace)}(?![\p{{L}}\p{{N}}_])"
            : Regex.Escape(StringToReplace);
        var options = CaseSensitive
            ? RegexOptions.CultureInvariant
            : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        return Regex.IsMatch(text, pattern, options);
    }

    /// <summary>
    ///     A road or suburb name. Matches the verified-pronunciation cap so the two
    ///     operator-facing pronunciation surfaces agree on what a "word" can be.
    /// </summary>
    public const int MaxWordLength = 80;

    /// <summary>
    ///     A respelling ("Kee-lor") or phoneme string. Generous for CMU/IPA, but bounded:
    ///     these ship to ElevenLabs on EVERY synthesis, and an unbounded replacement is
    ///     read aloud verbatim when the word appears — enough to blow the whole slot.
    /// </summary>
    public const int MaxReplacementLength = 200;

    private static Result<PronunciationRule>? LengthFailure(string word, string replacement)
    {
        if (word.Trim().Length > MaxWordLength)
            return Result<PronunciationRule>.Failure(
                $"A pronunciation rule's word must be at most {MaxWordLength} characters.");
        if (replacement.Trim().Length > MaxReplacementLength)
            return Result<PronunciationRule>.Failure(
                $"A pronunciation rule's replacement must be at most {MaxReplacementLength} characters.");
        return null;
    }

    /// <summary>Stable identity used to detect a changed rule set between versions.</summary>
    [JsonIgnore]
    public string Signature =>
        $"{Type}|{StringToReplace}|{Replacement}|{Alphabet}|{CaseSensitive}|{WordBoundaries}";

    /// <summary>Creates a validated alias (word → respelling) rule.</summary>
    public static Result<PronunciationRule> CreateAlias(
        string stringToReplace, string alias, bool caseSensitive = false, bool wordBoundaries = true)
    {
        if (string.IsNullOrWhiteSpace(stringToReplace))
            return Result<PronunciationRule>.Failure("A pronunciation rule needs a word to replace.");
        if (string.IsNullOrWhiteSpace(alias))
            return Result<PronunciationRule>.Failure("An alias rule needs a replacement (respelling).");
        if (LengthFailure(stringToReplace, alias) is { } aliasTooLong)
            return aliasTooLong;
        // A rule that replaces a word with itself changes nothing on air. It only pads the
        // list and makes a real rule harder to find, so it is rejected rather than stored
        // (matching is case-insensitive by default, so casing alone is not a difference).
        if (string.Equals(stringToReplace.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result<PronunciationRule>.Failure(
                $"'{stringToReplace.Trim()}' is respelled as itself, which changes nothing. " +
                "Enter how the word should sound, or remove the rule.");

        return Result<PronunciationRule>.Success(new PronunciationRule(
            PronunciationRuleType.Alias, stringToReplace.Trim(), alias.Trim(), null, caseSensitive, wordBoundaries));
    }

    /// <summary>Creates a validated phoneme (IPA/CMU) rule.</summary>
    public static Result<PronunciationRule> CreatePhoneme(
        string stringToReplace, string phoneme, string alphabet,
        bool caseSensitive = false, bool wordBoundaries = true)
    {
        if (string.IsNullOrWhiteSpace(stringToReplace))
            return Result<PronunciationRule>.Failure("A pronunciation rule needs a word to replace.");
        if (string.IsNullOrWhiteSpace(phoneme))
            return Result<PronunciationRule>.Failure("A phoneme rule needs a phoneme string.");
        if (LengthFailure(stringToReplace, phoneme) is { } phonemeTooLong)
            return phonemeTooLong;

        var normalizedAlphabet = (alphabet ?? string.Empty).Trim().ToLowerInvariant();
        if (!Alphabets.Contains(normalizedAlphabet))
            return Result<PronunciationRule>.Failure("A phoneme rule alphabet must be 'ipa' or 'cmu'.");

        return Result<PronunciationRule>.Success(new PronunciationRule(
            PronunciationRuleType.Phoneme, stringToReplace.Trim(), phoneme.Trim(), normalizedAlphabet,
            caseSensitive, wordBoundaries));
    }

    // ── Inline phoneme tags (per-rule preview) ─────────────────────────────────────
    // The whole-dictionary preview attaches a SAVED dictionary (locators); the per-rule
    // preview has no locator, so a phoneme rule's pronunciation must ride IN the text.
    // Which models read an inline tag is a DIFFERENT capability from which models apply
    // dictionary-attached phoneme rules (<see cref="AppliesOn" /> / <see cref="PhonemeRulesSafeFor" />,
    // which answer the airtime question): eleven_v3 reads slash-wrapped IPA natively while
    // ignoring dictionary phoneme rules; eleven_flash_v2 reads SSML <phoneme> tags; the
    // v2.5-family and multilingual models silently skip phonemes either way. Verified from
    // official ElevenLabs docs 2026-08-06 (full brief: agent://PhonemeLibrarian).

    /// <summary>The one v2-class model documented to render inline SSML <c>&lt;phoneme&gt;</c> tags.</summary>
    public const string InlineXmlPhonemeModel = "eleven_flash_v2";

    /// <summary>
    ///     Builds the text that makes <paramref name="model" /> speak <paramref name="word" /> with the
    ///     given phoneme WITHOUT any pronunciation dictionary attached — the per-rule preview
    ///     mechanism. Returns the tagged text to synthesize, or a failure naming exactly why the
    ///     (model, alphabet) pair cannot render inline (that message is what the operator sees on
    ///     400, so it must say which models CAN render tags). Alias rules never need this: their
    ///     replacement IS the text that airs, on every model.
    /// </summary>
    public static Result<string> BuildInlinePhonemeText(
        string model, string alphabet, string word, string phoneme)
    {
        // The v3 family reads slash-wrapped IPA inline; there is no CMU slash form, so a CMU
        // rule must go through the whole-dictionary preview (locator path) or be rewritten as IPA.
        if (model.Contains("v3", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(alphabet, "ipa", StringComparison.OrdinalIgnoreCase))
                return Result<string>.Failure(
                    $"Model '{model}' reads inline IPA only — a CMU phoneme cannot render as a v3 tag. " +
                    "Switch the rule to IPA, or save it and preview the whole dictionary.");
            return Result<string>.Success($"/{phoneme}/");
        }

        if (string.Equals(model.Trim(), InlineXmlPhonemeModel, StringComparison.OrdinalIgnoreCase))
        {
            // SSML <phoneme> tag: the alphabet attribute is 'ipa' or 'cmu-arpabet' (the rule's
            // domain 'cmu' is the same claim under its SSML spelling).
            string ssmlAlphabet = string.Equals(alphabet, "cmu", StringComparison.OrdinalIgnoreCase)
                ? "cmu-arpabet"
                : "ipa";
            return Result<string>.Success(
                $"<phoneme alphabet=\"{ssmlAlphabet}\" ph=\"{EscapeXmlAttribute(phoneme)}\">" +
                $"{EscapeXmlText(word)}</phoneme>");
        }

        // The 2.5-family and multilingual models skip phoneme tags silently (falling back to the
        // default pronunciation), so a preview would air the word WRONG without erroring — refuse
        // instead, naming the renderers. Note eleven_multilingual_v2 is additionally unsafe for a
        // dictionary-attached phoneme rule (see <see cref="PhonemeRulesSafeFor" />).
        return Result<string>.Failure(
            $"Model '{model}' does not render inline phoneme tags — phoneme rules are silently " +
            $"ignored there. Preview with an eleven_v3 voice (inline IPA) or {InlineXmlPhonemeModel} " +
            "(SSML <phoneme>), or use an alias respelling.");
    }

    private static string EscapeXmlText(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeXmlAttribute(string value) =>
        EscapeXmlText(value).Replace("\"", "&quot;");
}
