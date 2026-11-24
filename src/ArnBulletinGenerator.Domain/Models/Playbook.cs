using System.Text;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     What a station tells a role, as editable blocks rather than one file we own.
///     <para>
///         The predecessor was a markdown file in our repo with a handful of station knobs bolted
///         alongside it, and the seam showed: an instruction we hardcoded ("keep sentences under 24
///         words") silently overrode one a station authored ("mix your sentence lengths"), because
///         ours rendered from a default and theirs was prose. Splitting the prompt into named blocks
///         a station can rewrite, disable, reorder or extend removes the seam - there is no longer a
///         layer of ours sitting above theirs.
///     </para>
///     <para>
///         This is safe to hand over because grounding is not enforced here. Every claim is checked
///         against the supplied evidence by <c>ConversationalChecks</c> and by the editor, in code,
///         whatever the prompt says. A station that deletes every instruction gets a worse bulletin,
///         never an ungrounded one. The single exception is <see cref="PlaybookBlock.Locked" />,
///         reserved for the response contract: break that and the reply cannot be parsed at all.
///     </para>
/// </summary>
public sealed record Playbook(IReadOnlyList<PlaybookBlock> Blocks)
{
    public static Playbook Empty { get; } = new([]);

    /// <summary>Blocks in the order they will be rendered: by section, then by the station's order.</summary>
    public IReadOnlyList<PlaybookBlock> Ordered =>
        Blocks.Where(b => b.Enabled)
            .OrderBy(b => (int)b.Section)
            .ThenBy(b => b.Order)
            .ToList();

    /// <summary>The prompt as the model receives it.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        PlaybookSection? current = null;

        foreach (PlaybookBlock block in Ordered)
        {
            if (current != block.Section)
            {
                if (current is not null)
                    sb.AppendLine();
                sb.AppendLine($"## {Heading(block.Section)}");
                sb.AppendLine();
                current = block.Section;
            }

            sb.AppendLine(block.Body.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     Applies a station's edits, reporting anything it refused rather than dropping it quietly.
    ///     <para>
    ///         Refusals are returned, never swallowed. A setting that appears to apply and does
    ///         nothing is the failure mode this whole design exists to remove.
    ///     </para>
    /// </summary>
    public PlaybookApplyResult Apply(PlaybookOverlay overlay)
    {
        var byId = Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var refused = new List<string>();

        foreach ((string id, string body) in overlay.Rewrite)
        {
            if (!byId.TryGetValue(id, out PlaybookBlock? block))
            {
                refused.Add($"rewrite '{id}': no such block");
                continue;
            }

            if (block.Locked)
            {
                refused.Add(RefusalFor("rewrite", id, block));
                continue;
            }

            byId[id] = block with { Body = body };
        }

        foreach (string id in overlay.Disable)
        {
            if (!byId.TryGetValue(id, out PlaybookBlock? block))
            {
                refused.Add($"disable '{id}': no such block");
                continue;
            }

            if (block.Locked)
            {
                refused.Add(RefusalFor("disable", id, block));
                continue;
            }

            byId[id] = block with { Enabled = false };
        }

        foreach ((string id, int order) in overlay.Reorder)
        {
            if (byId.TryGetValue(id, out PlaybookBlock? block))
                byId[id] = block with { Order = order };
            else
                refused.Add($"reorder '{id}': no such block");
        }

        foreach (PlaybookBlock added in overlay.Add)
        {
            if (byId.ContainsKey(added.Id))
            {
                refused.Add($"add '{added.Id}': a block with that id already exists");
                continue;
            }

            // The Output and Selection sections are machine contracts. Output protects the
            // response shape; Selection protects running order and required-content enforcement,
            // which belong to station configuration rather than prompt text.
            if (added.Section is PlaybookSection.Output or PlaybookSection.Selection)
            {
                refused.Add(added.Section == PlaybookSection.Output
                    ? $"add '{added.Id}': the Output section is the response contract and takes no station blocks"
                    : $"add '{added.Id}': the Selection section is contract territory - running order and required content are station configuration, not prompt text");
                continue;
            }

            // A station's own block is never locked: locking exists to protect the machine
            // contract we ship, not to privilege one of theirs.
            byId[added.Id] = added with { Locked = false };
        }

        return new PlaybookApplyResult(new Playbook([.. byId.Values]), refused);
    }

    private static string RefusalFor(string operation, string id, PlaybookBlock block) =>
        block.Section == PlaybookSection.Selection
            ? $"{operation} '{id}': the Selection section is contract territory - running order and required content are station configuration, not prompt text"
            : $"{operation} '{id}': locked, because the response contract is what makes the reply parseable";

    private static string Heading(PlaybookSection section) => section switch
    {
        PlaybookSection.Role => "Who you are",
        PlaybookSection.Material => "What you may say",
        PlaybookSection.Selection => "What goes in",
        PlaybookSection.Voice => "How it sounds",
        PlaybookSection.Length => "Reaching the slot",
        PlaybookSection.Output => "How to reply",
        _ => section.ToString()
    };
}

/// <summary>
///     The five questions a producer actually asks about a read, in their order in the prompt.
///     <para>
///         Deliberately few and deliberately plain. These are the tabs an ARN editor sees, so they
///         are named for the decision being made rather than for the code behind it.
///     </para>
/// </summary>
public enum PlaybookSection
{
    /// <summary>Who the model is and what standard it is held to.</summary>
    Role = 0,

    /// <summary>What counts as sayable: the evidence boundary and how values must be treated.</summary>
    Material = 1,

    /// <summary>Which stories make the read, and what happens when they do not all fit.</summary>
    Selection = 2,

    /// <summary>Tone, rhythm, sentence shape, vocabulary.</summary>
    Voice = 3,

    /// <summary>
    ///     How long the read runs, and what to do when it does not fit.
    ///     <para>
    ///         Split out of <see cref="Voice" /> on 2026-08-26. Folded in as prose among the tone
    ///         rules, the length ladder reached the target on 4 of 6 runs; under its own heading it
    ///         reached it on 6 of 6, against a baseline of 2. Length is not a matter of taste and
    ///         the model stops treating it as one when it is asked separately.
    ///     </para>
    /// </summary>
    Length = 4,

    /// <summary>The response contract. Carries the only blocks a station cannot change.</summary>
    Output = 5
}

/// <summary>One editable instruction.</summary>
/// <param name="Id">Stable, dotted, e.g. <c>voice.name-roads</c>. Overlays address blocks by this.</param>
/// <param name="Section">Which tab it belongs under.</param>
/// <param name="Body">The instruction, in markdown, exactly as the model will read it.</param>
/// <param name="Order">Position within its section.</param>
/// <param name="Locked">
///     True only where an edit would stop the reply being parseable. Everything a station could
///     reasonably want to change - including what counts as evidence - is unlocked, because the
///     deterministic checks enforce grounding regardless of what the prompt says.
/// </param>
/// <param name="Enabled">False when the station has switched the block off.</param>
public sealed record PlaybookBlock(
    string Id,
    PlaybookSection Section,
    string Body,
    int Order,
    bool Locked = false,
    bool Enabled = true);

/// <summary>A station's edits against the shipped default, stored as a diff rather than a copy.</summary>
/// <remarks>
///     Held as a diff so a station that never touched a block keeps receiving improvements to it.
///     A station that forked the whole prompt would silently freeze at the version they forked.
/// </remarks>
public sealed record PlaybookOverlay
{
    public static PlaybookOverlay None { get; } = new();

    /// <summary>Block id to replacement body.</summary>
    public IReadOnlyDictionary<string, string> Rewrite { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Block ids the station has switched off.
    ///     <para>
    ///         A list rather than a set because this is a persisted document, and
    ///         <c>System.Text.Json</c> cannot deserialise <c>IReadOnlySet&lt;T&gt;</c>. Set
    ///         semantics would have to be bought with a converter registered globally in
    ///         <c>JsonDefaults</c>, which is a change to how every stored type serialises in
    ///         exchange for a guarantee nothing here needs: disabling a block twice is the same as
    ///         disabling it once, and ordering never mattered.
    ///     </para>
    /// </summary>
    public IReadOnlyList<string> Disable { get; init; } = [];

    /// <summary>Block id to new position within its section.</summary>
    public IReadOnlyDictionary<string, int> Reorder { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Instructions this station added that we never shipped.</summary>
    public IReadOnlyList<PlaybookBlock> Add { get; init; } = [];

    public bool IsEmpty =>
        Rewrite.Count == 0 && Disable.Count == 0 && Reorder.Count == 0 && Add.Count == 0;

    /// <summary>
    ///     Narrows a shared station overlay to one workflow role and strips the role qualifier from
    ///     every matching block id. Unqualified legacy keys are intentionally offered to every role:
    ///     before role qualification each such id existed in only one prompt, so the other prompts
    ///     simply refuse it rather than applying it to the wrong block.
    /// </summary>
    public PlaybookOverlay ForRole(string role)
    {
        string? canonicalRole = BulletinPlaybook.CanonicalRole(role);
        if (canonicalRole is null || IsEmpty)
            return None;

        return new PlaybookOverlay
        {
            Rewrite = Rewrite
                .Select(pair => Narrow(pair.Key, canonicalRole) is { } id
                    ? new KeyValuePair<string, string>(id, pair.Value)
                    : default)
                .Where(pair => pair.Key is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Disable = Disable
                .Select(id => Narrow(id, canonicalRole))
                .Where(id => id is not null)
                .Cast<string>()
                .ToArray(),
            Reorder = Reorder
                .Select(pair => Narrow(pair.Key, canonicalRole) is { } id
                    ? new KeyValuePair<string, int>(id, pair.Value)
                    : default)
                .Where(pair => pair.Key is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Add = Add
                .Select(block => Narrow(block.Id, canonicalRole) is { } id
                    ? block with { Id = id }
                    : null)
                .Where(block => block is not null)
                .Cast<PlaybookBlock>()
                .ToArray()
        };
    }

    private static string? Narrow(string id, string role)
    {
        int separator = id.IndexOf('/');
        if (separator < 0)
            return id;

        return string.Equals(id[..separator], role, StringComparison.OrdinalIgnoreCase)
            ? id[(separator + 1)..]
            : null;
    }

    /// <summary>
    ///     Value identity, used to decide whether an edit is a real change worth a new version.
    ///     <para>
    ///         Record equality compares the collections by reference, which would mint a version on
    ///         every save regardless of content, so the history would stop meaning anything.
    ///     </para>
    /// </summary>
    public string ConfigurationKey =>
        string.Join("|",
            string.Join(",", Rewrite.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}")),
            string.Join(",", Disable.OrderBy(d => d, StringComparer.Ordinal)),
            string.Join(",", Reorder.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}")),
            string.Join(",", Add.OrderBy(a => a.Id, StringComparer.Ordinal)
                .Select(a => $"{a.Id}:{(int)a.Section}:{a.Order}:{a.Enabled}:{a.Body}")));
}

/// <param name="Playbook">The resolved playbook.</param>
/// <param name="Refused">
///     Edits that could not be applied, each with its reason, for the API to return as a validation
///     error and the live path to record on the trace.
/// </param>
public sealed record PlaybookApplyResult(Playbook Playbook, IReadOnlyList<string> Refused);
