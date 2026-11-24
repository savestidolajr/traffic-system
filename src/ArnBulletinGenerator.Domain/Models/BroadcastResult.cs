namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Debug information from the broadcast generation process.
/// </summary>
public sealed record BroadcastDebugInfo(
    string ScriptGenerator,
    string AudioSynthesizer,
    double FinalDuration);

/// <summary>
///     Result of a successful broadcast generation.
///     All fields required.
/// </summary>
public sealed record BroadcastResult(
    string FileName,
    string Message,
    BroadcastDebugInfo DebugInfo);
