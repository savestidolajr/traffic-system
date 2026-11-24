namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The operator's pending script edit awaiting publication evidence: the script as generated
///     and the words the operator re-voiced it to. Written by JobService.RegenerateJobAsync
///     BEFORE AudioFinalizer.RevoiceExistingScriptAsync overwrites ScriptText; consumed (cleared
///     and enqueued for lesson learning) by PublishJobAsync only when playout delivery succeeded.
/// </summary>
public sealed record RevoiceObservation(
    DateTime CapturedAtUtc,
    string By,
    string OriginalScript,
    string EditedScript);
