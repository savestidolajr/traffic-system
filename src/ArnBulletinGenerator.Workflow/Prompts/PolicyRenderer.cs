using System.Text;
using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.Prompts;

/// <summary>
///     Renders ARN's configuration into prompt text, one method per stage that is allowed to see it.
///     <para>
///         Having exactly two methods here is the enforcement mechanism. Station policy controls
///         editorial direction and voice, while evidence and safety rules remain system-owned. The
///         predecessor pipeline passed one undifferentiated style blob to writer, reviewer and
///         refiner alike, which is how an instruction about which roads matter ended up in front of
///         an agent judging sentence grammar.
///     </para>
/// </summary>
public static class PolicyRenderer
{
    /// <summary>
    ///     Editorial direction for the PLANNER. Governs meaning: what is worth saying and in what
    ///     order. Deliberately carries no wording guidance, because the planner writes no prose and
    ///     any phrasing it saw would leak into a stage that must not produce English.
    /// </summary>
    public static string ForPlanner(EditorialPolicy policy)
    {
        if (policy.IsEmpty)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("STATION EDITORIAL POLICY (ARN-authored; governs WHAT is worth voicing, never how it is worded)");

        if (policy.PriorityRoads.Count > 0)
        {
            sb.AppendLine($"- Headline roads for this market: {string.Join(", ", policy.PriorityRoads)}. "
                          + "When the evidence carries one of these, it earns the lead unless something genuinely worse is present.");
        }

        if (!policy.IncludeClearRuns)
            sb.AppendLine("- This market does not air clear runs. Voice disruptions only.");

        if (policy.MaxStories > 0)
            sb.AppendLine($"- Plan at most {policy.MaxStories} stories.");

        if (policy.SectionOrder.Count > 0)
            sb.AppendLine($"- On-air section order: {string.Join(" then ", policy.SectionOrder)}.");

        if (policy.FreshnessWindow is { } window)
        {
            sb.AppendLine($"- Events older than about {Describe(window)} have usually stopped being news. "
                          + "Prefer fresher material unless an old event is still the biggest disruption.");
        }

        if (policy.Grouping.Enabled)
        {
            sb.AppendLine($"- Group candidates may carry {policy.Grouping.MinMembers} to {policy.Grouping.MaxMembers} members. "
                          + "A group is a CANDIDATE the fitter samples, not a commitment: it may voice a subset, "
                          + "so no member may depend on another being present.");
        }
        else
        {
            sb.AppendLine("- Do not group events in this market. Every event stands as its own story.");
        }

        if (policy.Notes.Length > 0)
        {
            sb.AppendLine("- Direction from the station's editors, which binds you:");
            sb.AppendLine($"  {policy.Notes.Trim()}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     Voice direction for the COMPOSER. Governs language only.
    ///     <para>
    ///         Explicitly states that voice settings cannot drop a fact. Without that line a station
    ///         could delete content through a wording preference, which would hand editorial
    ///         authority to the wrong stage and silently defeat the fitter's budget.
    ///     </para>
    /// </summary>
    public static string ForComposer(VoicePolicy policy)
    {
        if (policy.IsEmpty)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("STATION VOICE POLICY (ARN-authored; governs HOW the decided content is said)");
        sb.AppendLine("None of the following may change WHAT is voiced. The fitted plan has already decided that, "
                      + "and a wording preference never licenses dropping a fact.");

        if (policy.Tone.Length > 0)
            sb.AppendLine($"- Read tone: {policy.Tone}.");

        if (policy.RoadFeelPalette.Length > 0)
        {
            sb.AppendLine("- Road-feel vocabulary, banded by magnitude. This is the station's own wording and "
                          + "outranks your instincts; match the band the supplied figure falls in:");
            sb.AppendLine($"  {policy.RoadFeelPalette.Trim()}");
        }

        // Only what the station actually chose. An unset knob rendering its default is how a
        // house style asking for varied sentence lengths silently capped them (2026-08-26).
        if (policy.MaxSentenceWords is { } ceiling)
            sb.AppendLine($"- Keep sentences at or under {ceiling} spoken words.");

        if (policy.MaxDelayFigures > 0)
        {
            sb.AppendLine($"- Voice at most {policy.MaxDelayFigures} delay figure"
                          + $"{(policy.MaxDelayFigures == 1 ? string.Empty : "s")} in the read, on the items that most "
                          + "need one. Say the others in words rather than dropping them.");
        }

        sb.AppendLine($"- Transitions: {DescribeTransitions(policy.Transitions)}");

        if (policy.PreferredPhrases.Count > 0)
            sb.AppendLine($"- Wording this station likes, where it genuinely fits: {string.Join("; ", policy.PreferredPhrases)}.");

        if (policy.AvoidPhrases.Count > 0)
        {
            sb.AppendLine($"- Wording this station has ruled out: {string.Join("; ", policy.AvoidPhrases)}. "
                          + "Say the same fact differently rather than dropping it.");
        }

        if (policy.Examples.Count > 0)
        {
            sb.AppendLine("- Reference reads showing the shape this station wants. Copy the SHAPE, never the words:");
            foreach (string example in policy.Examples)
                sb.AppendLine($"  \"{example.Trim()}\"");
        }

        if (policy.Notes.Length > 0)
        {
            sb.AppendLine("- Direction from the station's editors, which binds you:");
            sb.AppendLine($"  {policy.Notes.Trim()}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeTransitions(TransitionDensity density) => density switch
    {
        TransitionDensity.Minimal =>
            "keep items separate. Join only where two facts are genuinely one thought.",
        TransitionDensity.Connected =>
            "prefer a continuous read. Join wherever a real relationship exists in the data, and never invent one to make a join.",
        _ =>
            "join where the data genuinely relates, and let an item stand alone where it does not. "
            + "A connective that could be deleted without changing the meaning is filler."
    };

    private static string Describe(TimeSpan window) =>
        window.TotalHours >= 1
            ? $"{window.TotalHours:0.#} hour{(window.TotalHours >= 2 ? "s" : string.Empty)}"
            : $"{window.TotalMinutes:0} minutes";
}
