using ArnBulletinGenerator.Domain.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Reads a shipped default playbook from its markdown file.
///     <para>
///         Defaults stay on disk rather than becoming string literals for the reason the prompt
///         files always have: an editorial change must be reviewable as a diff by someone who does
///         not read C#. The block markers add the structure a station needs to address one
///         instruction without forking the file.
///     </para>
///     <para>
///         Marker syntax, one line above each block:
///         <code>&lt;!-- block: voice.name-roads | section: Voice --&gt;</code>
///         with <c>| locked</c> appended for the response contract. Text before the first marker is
///         a file comment and is not sent to the model.
///     </para>
/// </summary>
public static partial class PlaybookParser
{
    [GeneratedRegex(@"^<!--\s*block:\s*(?<id>[a-z0-9.\-]+)\s*\|\s*section:\s*(?<section>[A-Za-z]+)\s*(?<locked>\|\s*locked\s*)?-->\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkerPattern { get; }
    /// <summary>
    ///     These rules are enforced by code as well as described in the prompt. Keeping their lock
    ///     here prevents an operator from removing a marker and making the editable prompt disagree
    ///     with the runtime checks.
    /// </summary>
    private static readonly IReadOnlySet<string> CodeLockedBlockIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "output.format",
        "selection.required",
        "selection.overshoot",
        "material.facts-only",
        "material.values-are-exact",
        "material.ownership",
        "material.reasoning"
    };


    /// <summary>Parses annotated markdown into an ordered playbook.</summary>
    /// <exception cref="FormatException">A marker names a section that does not exist.</exception>
    public static Playbook Parse(string markdown)
    {
        var blocks = new List<PlaybookBlock>();
        var body = new StringBuilder();
        string? id = null;
        PlaybookSection section = PlaybookSection.Role;
        bool locked = false;
        var orderBySection = new Dictionary<PlaybookSection, int>();

        void Flush()
        {
            if (id is null)
                return;

            string text = body.ToString().Trim();
            if (text.Length > 0)
            {
                orderBySection.TryGetValue(section, out int next);
                orderBySection[section] = next + 1;
                blocks.Add(new PlaybookBlock(id, section, text, next, locked || CodeLockedBlockIds.Contains(id)));
            }

            body.Clear();
        }

        foreach (string line in markdown.Split('\n'))
        {
            Match marker = MarkerPattern.Match(line.TrimEnd('\r'));
            if (!marker.Success)
            {
                if (id is not null)
                    body.AppendLine(line.TrimEnd('\r'));
                continue;
            }

            Flush();
            id = marker.Groups["id"].Value;
            locked = marker.Groups["locked"].Success;
            string name = marker.Groups["section"].Value;
            if (!Enum.TryParse(name, ignoreCase: true, out section))
                throw new FormatException($"Playbook block '{id}' names unknown section '{name}'.");
        }

        Flush();
        return new Playbook(blocks);
    }
}
