namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration for synthesis history storage. Production uses the table-backed
///     <c>TableStorageSynthesisHistoryRepository</c> with <c>StorageOptions</c>; the optional
///     JSON path is used only by the in-memory repository for local or test scenarios.
/// </summary>
public sealed class SynthesisHistoryOptions
{
    public const string SectionName = "SynthesisHistory";

    /// <summary>
    ///     Local JSON file path for persistence.
    ///     When non-empty, <c>InMemorySynthesisHistoryRepository</c> writes and reads from this path.
    ///     Example: "data/synthesis-history.json" (relative to BaseDirectory).
    ///     Leave empty or null to keep history ephemeral (in-memory only).
    /// </summary>
    public string JsonFilePath { get; set; } = "";

}
