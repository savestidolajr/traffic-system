namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Named payload families that may be moved off the hot aggregate row.</summary>
public enum JobPayloadKind
{
    GeneratorInput,
    Incidents,
    AllIncidents,
    Corridors,
    FlowRoads,
    ScriptSentences,
    WordTimings,
    Evidence
}

/// <summary>Stable reference to an immutable compressed job payload.</summary>
public sealed record JobPayloadReference(
    string JobId,
    JobPayloadKind Kind,
    string Reference,
    string ContentHash);
