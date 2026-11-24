namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Closed taxonomy for why an operator took a bulletin off the air path.</summary>
public enum RejectionReasonCode
{
    Pronunciation = 0,
    WrongOrMissingIncident = 1,
    EditorialPriority = 2,
    ContentSelection = 3,
    WordingTone = 4,
    ScriptStructure = 5,
    AudioUnusable = 6,
    TimingLength = 7,
    RevisionRequested = 8
}

/// <summary>Records an operator's rejection of a Job's bulletin, including the reason and the exact script text that was rejected.</summary>
public sealed record RejectionEntry(
    string Reason,
    string Notes,
    DateTime At,
    string By,
    Option<string> RejectedScript = default);
