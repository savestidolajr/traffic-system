namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Operator market-assignment DTOs (Workshop 2 / SDD §4.2 — operator scoping)
// =============================================================================

public sealed record OperatorAssignmentDto(
    string ObjectId,
    string DisplayName,
    IReadOnlyList<string> Markets,
    string UpdatedAt,
    string UpdatedBy);

/// <summary>Wire DTO — validated at the trigger boundary.</summary>
public sealed record UpsertOperatorAssignmentRequest(
    IReadOnlyList<string>? Markets = null,
    string? DisplayName = null);

/// <summary>
///     The calling operator's own scope. <see cref="Scoped" /> is true only when a non-empty
///     market assignment narrows the caller. Admins, operators without an assignment row, and
///     operators with an empty market list are unrestricted and return <c>Scoped = false</c>.
/// </summary>
public sealed record OperatorScopeDto(bool Scoped, IReadOnlyList<string>? Markets);

public sealed record OperatorDirectoryEntryDto(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    IReadOnlyList<string> Roles,
    string Source,
    IReadOnlyList<string> Markets,
    bool IsAdmin,
    string? UpdatedAtUtc,
    string? UpdatedBy);

public sealed record OperatorDirectoryDto(
    IReadOnlyList<OperatorDirectoryEntryDto> Operators,
    string DirectoryStatus,
    string? DirectoryError);

/// <summary>
///     Result of an operator-assignment mutation (e.g. delete). Distinct from
///     <c>JobActionResponseDto</c> so the second field is honestly named — it carries the
///     operator's Azure AD object id, not a job id (QT-013, 2026-07-05).
/// </summary>
public sealed record OperatorActionResponseDto(
    string Status,
    string ObjectId,
    string Message);

// =============================================================================
// Priority-roads DTOs (Workshop 2 / SDD §4.2 — good-run reporting)
// =============================================================================

/// <summary>Wire DTO. <c>FlowTier</c> is "none" | "normal" | "high" | "critical" (flow slot competitiveness).</summary>
public sealed record PriorityRoadDto(string Name, bool BoostIncidents = true, string FlowTier = "none", bool Enabled = true);

public sealed record PriorityRoadsDto(string Market, IReadOnlyList<PriorityRoadDto> Roads);

/// <summary>Wire DTO — validated at the trigger boundary.</summary>
public sealed record UpsertPriorityRoadsRequest(IReadOnlyList<PriorityRoadDto>? Roads = null);

// =============================================================================
// Filler-statements DTOs (BFN-002 — remaining-slot fill)
// =============================================================================

/// <summary>
///     Optional station-local eligibility window for a filler statement; any part omitted is
///     unconstrained (null Availability on the statement = always eligible). Dates are ISO
///     "yyyy-MM-dd" strings, times "HH:mm" strings (the same convention as
///     <see cref="ScheduleContracts.VoiceWindowDto"/>), days the .NET day names
///     "Sunday".."Saturday". A time window with End &lt;= Start wraps past midnight.
/// </summary>
public sealed record FillerAvailabilityDto(
    string? StartDate = null,
    string? EndDate = null,
    IReadOnlyList<string>? Days = null,
    string? StartLocal = null,
    string? EndLocal = null);

public sealed record FillerStatementDto(
    string Id,
    string Text,
    bool Enabled = true,
    FillerAvailabilityDto? Availability = null);

public sealed record FillerStatementsDto(string Market, IReadOnlyList<FillerStatementDto> Statements);

/// <summary>Wire DTO — validated at the trigger boundary.</summary>
public sealed record UpsertFillerStatementsRequest(IReadOnlyList<FillerStatementDto>? Statements = null);

// =============================================================================
// Verified-pronunciation DTOs — operator verdicts that mute a recurring
// audio-pre-review suspect word (see VerifiedPronunciation).
// =============================================================================

public sealed record VerifiedPronunciationDto(
    string Word,
    string? Note = null,
    bool Enabled = true,
    string? VerifiedBy = null,
    string? VerifiedAt = null);

public sealed record VerifiedPronunciationsDto(string Market, IReadOnlyList<VerifiedPronunciationDto> Words);

/// <summary>Wire DTO — validated at the trigger boundary.</summary>
public sealed record UpsertVerifiedPronunciationsRequest(IReadOnlyList<VerifiedPronunciationDto>? Words = null);
