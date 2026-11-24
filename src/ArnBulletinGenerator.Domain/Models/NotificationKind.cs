namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Operator-facing notification tiers (decided 2026-07-05 after the four-critic card
///     design review — see docs/operations/notifications.md "Card system").
///     Each kind maps to exactly one visual card tier; Suppressed sends nothing.
/// </summary>
public enum NotificationKind
{
    /// <summary>Bulletin awaiting review — grey emphasis card with Review button.</summary>
    ReadyForReview,

    /// <summary>First escalation tier (NotifyBeforePlayoutMinutes) — ready card with a reminder marker.</summary>
    ApprovalReminder,

    /// <summary>Final escalation tier (EscalationBeforePlayoutMinutes) — red card, verb-first, absolute deadline.</summary>
    UrgentApproval,

    /// <summary>Failure or missed window covered by a same-day re-air — quiet one-line card, no button.</summary>
    FailedCovered,

    /// <summary>Failure with filler (or nothing) queued — red intervention card; the operator owns the next step.</summary>
    FailedNotCovered,

    /// <summary>Deadline passed after an urgent escalation; fallback aired — quiet closure card.</summary>
    MissedDeadlineClosure,

    /// <summary>No card. Approved/Published/Rejected and routine content-limited fallbacks are audit noise.</summary>
    Suppressed
}
