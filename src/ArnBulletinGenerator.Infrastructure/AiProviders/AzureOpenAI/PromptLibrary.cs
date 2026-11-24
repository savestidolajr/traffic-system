using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using System.Text;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Owns the surviving on-disk templates plus pure render helpers that project runtime data
///     into those templates.
/// </summary>
internal static class PromptLibrary
{
    // Surviving raw-model roles historically inherited prompts/system.md when no role-specific
    // override was supplied. Keep this shared base explicit; role-specific callers override it.
    internal static readonly string SystemInstructions = LoadPrompt("system");
    internal static readonly string ReviewTemplate = LoadPrompt("review");
    internal static readonly string RefineSentenceTemplate = LoadPrompt("refine-sentence");
    internal static readonly string SafetyTemplate = LoadPrompt("safety");
    internal static readonly string DirectorTemplate = LoadPrompt("director");
    // Rewrites an approved bulletin into a requested register. Unlike the director it MAY change
    // wording, because the words are the strongest delivery lever on this voice (verified
    // 2026-08-03) - so its output is operator-reviewed and safety-reviewed before it airs.
    internal static readonly string DeliveryWriterTemplate = LoadPrompt("delivery-writer");
    internal static readonly string DeliveryWriterSystem = LoadPrompt("delivery-writer-system");

    // Rewrites an approved bulletin in place to follow a free-form operator instruction (content
    // pass: drop/reorder/reword/re-emphasise). It is a lightweight preview editor: the caller
    // safety-checks it, and the operator applies it through the verbatim re-voice path.
    internal static readonly string RewriteTemplate = LoadPrompt("rewrite");
    /// <summary>
    ///     System instructions for the lightweight content rewriter. It deliberately does not inherit
    ///     the composed bulletin-writer prompt or ask the model to fact-check the operator's request.
    ///     The request carries the current script, compact reference data and word limit.
    /// </summary>
    internal static readonly string RewriteSystem = LoadPrompt("rewrite-system");



    private static string LoadPrompt(string name)
    {
        // ARN_PROMPTS_DIR overrides the deployed prompts/ directory for the whole process — the eval
        // harness uses it to inject an experimental prompt set without touching the tree a human
        // may be editing concurrently. Prompts are static-initialized once, so the override must
        // be set before the first generator use (e.g. before `dotnet test`).
        string? overrideDir = Environment.GetEnvironmentVariable("ARN_PROMPTS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var overrideCandidate = Path.Combine(overrideDir, $"{name}.md");
            if (File.Exists(overrideCandidate))
                return File.ReadAllText(overrideCandidate).Trim();
            throw new FileNotFoundException(
                $"ARN_PROMPTS_DIR is set to '{overrideDir}' but '{name}.md' was not found there. " +
                "The override directory must contain the complete prompt set.");
        }

        // Published and test applications place the content directly under AppContext.BaseDirectory.
        // The parent walk is only a source-tree fallback for local tooling; it never depends on the
        // process working directory and therefore cannot change when a host launches the Function
        // worker from another directory.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "prompts", $"{name}.md");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Trim();
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Required prompt file 'prompts/{name}.md' not found. " +
            $"Searched upward from {AppContext.BaseDirectory}. " +
            "Ensure the prompts/ directory is included in build and publish output.");
    }

    /// <summary>
    ///     Renders evidence units in a source-complete form for prompts. The cited id remains the
    ///     schema enum value, but the model also sees the source road/type and value explicitly, so
    ///     speakable facts such as road names or directions are never present only inside the id.
    ///     Corridor suburbs are repeated on every rendered unit for that corridor so identical road
    ///     names remain distinguishable when a bulletin contains multiple corridors on one road.
    /// </summary>
    internal static string RenderEvidenceUnits(IReadOnlyList<EvidenceUnit> units)
    {
        var suburbsByCorridor = units
            .Where(unit => unit.SourceKey.StartsWith("corridor:", StringComparison.OrdinalIgnoreCase) && unit.Field == "suburb")
            .GroupBy(CorridorIdPrefix, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        // Grouped by source, one heading per road, because the flat one-line-per-fact rendering
        // repeated "source=<road> near <suburb>; field=" on EVERY line: measured 2026-08-20 on a
        // live cairns draft, ~60 chars of boilerplate x 48 units, and the word "corridor" 88 times
        // in a 5,915-char prompt — the exact token MetaLeakDetector exists to catch coming back out
        // of the writer. Ids are still printed VERBATIM and remain the only citable handle, so this
        // is a rendering change only: the draft contract is untouched.
        var builder = new StringBuilder();
        foreach (var group in units.GroupBy(unit => unit.SourceKey, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
                builder.Append('\n');
            // "# " marks the heading as a label, not a handle. Bare, it sat on its own line
            // immediately above the bracketed ids with nothing to tell the two apart, and models
            // duly cited it ("M5, Kingsgrove") — 41% of captured refine calls. The bracket is the
            // citable form; the hash says this line is not one.
            builder.Append("# ")
                   .Append(SourceHeading(group.Key, CorridorIdPrefix(group.First()), suburbsByCorridor))
                   .Append('\n');
            foreach (var unit in group)
                builder.Append("    [").Append(unit.Id).Append("] ")
                       .Append(unit.Field).Append(" = ").Append(unit.Value).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>
    ///     The road as a presenter would name it — never our own bucket word. The former rendering
    ///     prefixed every line with the literal source kind ("corridor Pittwater Road"), handing the
    ///     writer internal vocabulary it then echoed on air.
    /// </summary>
    private static string SourceHeading(
        string sourceKey, string corridorIdPrefix, IReadOnlyDictionary<string, string> suburbsByCorridor)
    {
        string heading = sourceKey;
        foreach (string prefix in new[] { "corridor:", "route:", "cleared:", "goodrun:" })
        {
            if (heading.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                heading = RoadNameFormatter.ToSpeakable(heading[prefix.Length..]);
                break;
            }
        }

        if (corridorIdPrefix.Length > 0
            && suburbsByCorridor.TryGetValue(corridorIdPrefix, out string? suburb))
            heading += $", {suburb}";

        return heading;
    }

    /// <summary>
    ///     The corridor part of an evidence id — "r1" from "r1:delayMinutes". Ids stopped carrying
    ///     "corridor:&lt;road&gt;#&lt;n&gt;" on 2026-08-20: the road is named once by the group
    ///     heading, so an id only needs to be unique and copyable. Weather/fuel/good-run ids share
    ///     the same "&lt;source&gt;:&lt;field&gt;" shape and resolve to their own source key.
    /// </summary>
    private static string CorridorIdPrefix(EvidenceUnit unit)
    {
        int fieldStart = unit.Id.IndexOf(':');
        return fieldStart > 0 ? unit.Id[..fieldStart] : string.Empty;
    }

}
