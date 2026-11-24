using System.Text;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Renders a station's playbook edits as AMENDMENTS to instructions the model already holds.
///     <para>
///         The deployed writer's standing instructions live in its Foundry agent, not in the
///         request, so the request cannot simply carry a rewritten prompt: it would arrive
///         alongside the original rather than instead of it. What the request CAN carry is the
///         difference. Sending only the delta also keeps the cost proportional to what a station
///         actually changed, which for most stations is nothing.
///     </para>
///     <para>
///         Amendments are phrased as overrides rather than as suggestions because they are
///         resolving a conflict with text the model can still see. "This replaces..." beats
///         "prefer..." when both are in context and one has to win.
///     </para>
/// </summary>
public static class PlaybookAmendmentRenderer
{
    /// <summary>
    ///     The amendment block, or empty when the station changed nothing. Refusals are returned
    ///     separately so the caller can log them rather than silently honouring a partial edit.
    /// </summary>
    public static (string Text, IReadOnlyList<string> Refused) Render(Playbook shipped, PlaybookOverlay overlay)
    {
        if (overlay is null || overlay.IsEmpty)
            return (string.Empty, []);

        PlaybookApplyResult applied = shipped.Apply(overlay);
        var shippedById = shipped.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("STATION PLAYBOOK AMENDMENTS (trusted station configuration).");
        sb.AppendLine("Your standing instructions are amended below for this station. Where an amendment "
                      + "conflicts with a standing instruction, the amendment wins.");
        sb.AppendLine();

        foreach ((string id, string body) in overlay.Rewrite.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (!shippedById.TryGetValue(id, out PlaybookBlock? block) || block.Locked)
                continue;

            sb.AppendLine($"REPLACED ({Describe(block.Section)}):");
            sb.AppendLine(body.Trim());
            sb.AppendLine();
        }

        var withdrawn = overlay.Disable
            .Where(id => shippedById.TryGetValue(id, out PlaybookBlock? b) && !b.Locked)
            .Select(id => shippedById[id])
            .ToList();
        if (withdrawn.Count > 0)
        {
            sb.AppendLine("WITHDRAWN. These standing instructions do not apply to this station:");
            foreach (PlaybookBlock block in withdrawn)
                sb.AppendLine($"- {FirstLine(block.Body)}");
            sb.AppendLine();
        }

        foreach (PlaybookBlock added in overlay.Add.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"ADDED ({Describe(added.Section)}):");
            sb.AppendLine(added.Body.Trim());
            sb.AppendLine();
        }

        return (sb.ToString().TrimEnd(), applied.Refused);
    }

    /// <summary>
    ///     Names the amended instruction by the concern it governs, so the model can locate what is
    ///     being overridden without being handed the original text back.
    /// </summary>
    private static string Describe(PlaybookSection section) => section switch
    {
        PlaybookSection.Role => "your role",
        PlaybookSection.Material => "what you may say",
        PlaybookSection.Selection => "what goes in",
        PlaybookSection.Voice => "how it sounds",
        PlaybookSection.Output => "how to reply",
        _ => section.ToString()
    };

    /// <summary>
    ///     A withdrawn instruction is identified, not reproduced. Quoting it in full to say it no
    ///     longer applies puts the rule back in context in the same breath as cancelling it.
    /// </summary>
    private static string FirstLine(string body)
    {
        string line = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('<')) ?? body.Trim();

        return line.Length <= 120 ? line : line[..117].TrimEnd() + "...";
    }
}
