using System.Text;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     A bounded presentation blueprint for one read: it varies HOW the bulletin is organised —
///     opener framing, item flow, where clear roads sit, and how it closes — while the severity
///     lead and every fact stay immutable (the summarizer's worst-first ordering and the draft
///     rules enforce that). Recent diverse-generation work (Where You Inject Diversity Matters,
///     Jun 2026) shows structural variety comes from varying an explicit intermediate specification,
///     not from temperature: a fixed evidence outline yields the same skeleton every run regardless
///     of sampling (measured live here: identical inputs gave word-Jaccard ~0.55 but an identical
///     structure). Selection is DETERMINISTIC per job seed — a read is reproducible — yet rotates
///     across jobs so the network never hears the same shape twice running. It replaces the earlier
///     single random "variety angle" nudge with a four-dimension, seed-stable specification.
/// </summary>
public sealed record BulletinBlueprint(string Opener, string Flow, string ClearRoads, string Close)
{
    /// <summary>Compact directive rendered into the draft prompt's blueprint slot.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("This read's SHAPE (organisation only — the worst item still leads and every fact stays exactly as given):");
        sb.Append("- Opener: ").AppendLine(Opener);
        sb.Append("- Flow: ").AppendLine(Flow);
        sb.Append("- Clear roads: ").AppendLine(ClearRoads);
        sb.Append("- Close: ").Append(Close);
        return sb.ToString();
    }
}

public static partial class BlueprintSelector
{
    private static readonly string[] Openers =
    [
        "Name the worst road and its state plainly, then move through the rest.",
        "Lead with how the worst spot FEELS — the severity phrase — before you name the road.",
        "Place the listener at the worst spot or suburb, then say what's happening there.",
        "Open on the single biggest disruption and how far it reaches, then the rest.",
    ];

    private static readonly string[] AllClearOpeners =
    [
        "Open on the overall clear picture in one line, then name the roads as the evidence.",
        "Lead with the good news plainly, then the named roads that back it up.",
    ];

    private static readonly string[] Flows =
    [
        "Group roads that share a state into one clause; take the rest as short discrete sentences.",
        "Keep it clipped this read — short, punchy, anchored sentences with few linking words.",
        "Ease between items with light additive connectives; never stack the same one twice.",
        "Vary which of the equal-severity items you voice first, away from the obvious order.",
    ];

    private static readonly string[] ClearBusy =
    [
        "At most one brief clear-road aside near the end — or leave them out entirely.",
        "Leave clear roads out this read; the disruptions are the whole story.",
    ];

    private static readonly string[] ClearQuiet =
    [
        "Clear roads are real content — name them plainly, grouped naturally, then the issue.",
        "Build the read around the clear roads and fold the one or two issues in.",
    ];

    private static readonly string[] Closes =
    [
        "End on the last real traffic fact — no wrap-up, no summary tail.",
        "Close on the strongest single item; never restate what you've already said.",
    ];

    /// <summary>
    ///     Picks a blueprint for a read. <paramref name="seed" /> is the job id (or a per-candidate
    ///     variant) so the choice is stable per job but differs across jobs and candidates.
    ///     <paramref name="voiceableIncidentCount" /> classifies the read: 0 = all-clear (clear roads
    ///     ARE the read), 1-2 = quiet, 3+ = busy.
    /// </summary>
    public static BulletinBlueprint Select(string? seed, int voiceableIncidentCount)
    {
        bool allClear = voiceableIncidentCount == 0;
        bool busy = voiceableIncidentCount >= 3;
        int h = StableHash(seed);

        string opener = allClear ? Pick(AllClearOpeners, h) : Pick(Openers, h);
        string flow = Pick(Flows, h >> 4);
        string clear = allClear
            ? "Clear roads ARE the read — lead with the picture and build on the named roads."
            : busy ? Pick(ClearBusy, h >> 8) : Pick(ClearQuiet, h >> 8);
        string close = Pick(Closes, h >> 12);

        return new BulletinBlueprint(opener, flow, clear, close);
    }

    private static string Pick(string[] options, int h) => options[(h & 0x7fffffff) % options.Length];

    // Stable across processes and runs (unlike string.GetHashCode, which is randomised per run) so a
    // given job's shape is reproducible for diagnostics and replay.
    private static int StableHash(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
            return 0;
        unchecked
        {
            int h = 17;
            foreach (char c in seed)
                h = (h * 31) + c;
            return h;
        }
    }
}
