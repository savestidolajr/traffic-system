namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A named, versioned set of station edits to the instructions a bulletin role is given —
///     the unit of the shared playbook library.
///     <para>
///         Stored as a <see cref="PlaybookOverlay" /> (a diff) rather than as a whole prompt.
///         A station that never touched a block keeps receiving our improvements to it; one that
///         forked the entire prompt would silently freeze at the version they forked. The
///         consequence is that the shipped default remains ours to improve and the station's
///         intent remains theirs to state, without either overwriting the other.
///     </para>
///     <para>
///         Mirrors <see cref="DeliveryStyle" />: append-only versions, edit mints a version only
///         when the overlay actually changes, archive hides without minting.
///     </para>
/// </summary>
public sealed class BulletinPlaybook
{
    private readonly List<BulletinPlaybookVersion> _versions;

    private BulletinPlaybook(
        string id,
        string name,
        string? description,
        string role,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<BulletinPlaybookVersion> versions)
    {
        Id = id;
        Name = name;
        Description = description;
        Role = role;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CreatedBy = createdBy;
        AssignedStations = assignedStations;
        _versions = versions;
    }

    public string Id { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Which role's instructions this edits, e.g. <c>writer</c>.</summary>
    public string Role { get; }

    public string Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? CreatedBy { get; }
    public IReadOnlyList<string> AssignedStations { get; private set; }
    public IReadOnlyList<BulletinPlaybookVersion> Versions => _versions;

    public int CurrentVersion => _versions.Count == 0 ? 0 : _versions[^1].Version;

    /// <summary>The overlay of the current (latest) version, or no edits at all.</summary>
    public PlaybookOverlay CurrentOverlay =>
        _versions.Count == 0 ? PlaybookOverlay.None : _versions[^1].Overlay;

    /// <summary>
    ///     Roles a playbook may target. Anything else is a typo, not a new capability.
    ///     <para>
    ///         <c>writer</c> is the MATRIX pipeline's prompt. Dev has run the conversational
    ///         pipeline since 2026-08-26, so for a while this list allowed operators to edit only a
    ///         prompt that no longer generated anything. The other four are the prompts that
    ///         actually write today's bulletins.
    ///     </para>
    ///     <para>
    ///         A role here must resolve to a block-marked prompt file in
    ///         <c>PlaybooksHttpTrigger.FindDefaultPrompt</c>, or the endpoint accepts the role and
    ///         then finds nothing to edit.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<string> SupportedRoles { get; } =
        ["writer", "compose2", "writeIncidents", "writeFlow", "edit"];

    /// <summary>
    ///     Resolves an external role name to the exact casing used in block qualifiers and prompt
    ///     files. Role names are case-insensitive at the API boundary, but stored values are
    ///     canonical so <c>writeFlow/...</c> keys survive every round trip unchanged.
    /// </summary>
    public static string? CanonicalRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return SupportedRoles.FirstOrDefault(
            supported => string.Equals(supported, role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static Result<BulletinPlaybook> Create(
        string name,
        string? description,
        string role,
        PlaybookOverlay overlay,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<BulletinPlaybook>.Failure("Playbook name is required.");
        if (overlay is null)
            return Result<BulletinPlaybook>.Failure("Playbook overlay is required.");

        string? canonicalRole = CanonicalRole(role);
        if (canonicalRole is null)
        {
            return Result<BulletinPlaybook>.Failure(
                $"Unknown playbook role '{role?.Trim()}'. Supported: {string.Join(", ", SupportedRoles)}.");
        }

        role = canonicalRole;

        id = string.IsNullOrWhiteSpace(id) ? $"pb-{Guid.NewGuid():N}" : id.Trim();
        var version1 = new BulletinPlaybookVersion(1, overlay, Normalize(changeNote), nowUtc, createdBy);
        return new BulletinPlaybook(id, name.Trim(), Normalize(description), role, "active",
            nowUtc, nowUtc, createdBy, [], [version1]);
    }

    /// <summary>
    ///     Applies a name/description edit and mints a version when the overlay differs. Returns
    ///     true when a version was minted.
    /// </summary>
    public bool ApplyEdit(
        string name,
        string? description,
        PlaybookOverlay overlay,
        string? changeNote,
        string? editedBy,
        DateTime nowUtc)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = Normalize(description);

        BulletinPlaybookVersion latest = _versions[^1];
        bool changed = latest.Overlay.ConfigurationKey != overlay.ConfigurationKey;
        if (changed)
            _versions.Add(new BulletinPlaybookVersion(latest.Version + 1, overlay, Normalize(changeNote), nowUtc, editedBy));

        UpdatedAt = nowUtc;
        return changed;
    }

    /// <summary>Mints a new version that copies a prior version's overlay (immutable restore).</summary>
    public Result RestoreVersion(int version, string? restoredBy, DateTime nowUtc)
    {
        BulletinPlaybookVersion? source = _versions.FirstOrDefault(v => v.Version == version);
        if (source is null)
            return Result.Failure($"Playbook version {version} not found.");

        _versions.Add(new BulletinPlaybookVersion(_versions[^1].Version + 1, source.Overlay,
            $"Restored from version {version}.", nowUtc, restoredBy));
        UpdatedAt = nowUtc;
        return Result.Success();
    }

    public void Archive(DateTime nowUtc)
    {
        Status = "archived";
        UpdatedAt = nowUtc;
    }

    public void Unarchive(DateTime nowUtc)
    {
        Status = "active";
        UpdatedAt = nowUtc;
    }

    /// <summary>Rehydrates from persisted data without re-running creation invariants.</summary>
    public static BulletinPlaybook Reconstitute(
        string id,
        string name,
        string? description,
        string role,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<BulletinPlaybookVersion> versions) =>
        new(id, name, description, role, status, createdAt, updatedAt, createdBy, assignedStations, versions);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
///     One immutable iteration of a playbook. Append-only, so the editorial history of how a
///     station's voice was tuned is never lost.
/// </summary>
public sealed record BulletinPlaybookVersion(
    int Version,
    PlaybookOverlay Overlay,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy);
