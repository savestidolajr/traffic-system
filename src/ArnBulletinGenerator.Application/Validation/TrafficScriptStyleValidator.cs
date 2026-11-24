using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Application.Validation;

/// <summary>
///     Result of validating a generated bulletin script against ATN style rules: banned closers,
///     dangling endings, repetitive scaffolding. Figure count is owned by the conversational
///     workflow's own gates and prompts.
/// </summary>
public sealed record TrafficScriptStyleValidationResult(
    bool IsValid,
    IReadOnlyList<string> Issues);

public static partial class TrafficScriptStyleValidator
{
    public static TrafficScriptStyleValidationResult Validate(string script)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(script))
            return new TrafficScriptStyleValidationResult(false, ["Script is empty."]);

        if (SpeakerLabelRegex().IsMatch(script))
            issues.Add("Remove speaker labels like 'Alex:' from the script.");

        if (DanglingEndingRegex().IsMatch(script))
            issues.Add("End on a complete clause — the script trails off with a dangling conjunction.");

        if (BannedCloserRegex().IsMatch(script))
            issues.Add("Remove closers/advice lines ('plan ahead', 'take care', 'stay safe', 'motorists warned').");

        if (HasRepetitiveClosureQueueScaffolding(script))
            issues.Add("Avoid repeating the same closure-and-queue sentence template across consecutive incidents.");

        if (LowercaseSentenceStartRegex().IsMatch(script))
            issues.Add("Remove broken trailing fragments; each sentence after punctuation should start cleanly.");

        return new TrafficScriptStyleValidationResult(issues.Count == 0, issues);
    }

    private static bool HasRepetitiveClosureQueueScaffolding(string script)
    {
        var sentences = SentenceSplitRegex()
            .Split(script)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        int consecutiveTemplateCount = 0;

        foreach (string sentence in sentences)
        {
            if (!ClosedQueueTemplateRegex().IsMatch(sentence))
            {
                consecutiveTemplateCount = 0;
                continue;
            }

            consecutiveTemplateCount++;
            if (consecutiveTemplateCount >= 2)
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"(?im)^\s*[A-Za-z][A-Za-z0-9 '\-]{0,20}:\s+")]
    private static partial Regex SpeakerLabelRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+[a-z]")]
    private static partial Regex LowercaseSentenceStartRegex();


    [GeneratedRegex(@"(?i)\b(and|also|but|or|with|as well as|plus|only|still|while)[\s.\u2026\u2014-]*$")]
    private static partial Regex DanglingEndingRegex();

    [GeneratedRegex(@"(?i)\b(plan ahead|take care|stay safe|allow extra time|drive safely|motorists (are )?warned|be careful out there)\b")]
    private static partial Regex BannedCloserRegex();
    [GeneratedRegex(@"(?ix)
        ^
        [^.!?;]{0,80}
        \bis\s+closed\b
        [^.!?;]{0,120}
        \b(queue|queues|banked\s+back|stretch(?:ing|es)?|tailback)\b
    ")]
    private static partial Regex ClosedQueueTemplateRegex();

    [GeneratedRegex(@"[.!?;]+")]
    private static partial Regex SentenceSplitRegex();
}
