namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configurable content-safety policy for generated scripts: banned words, place-name
///     exceptions that must not false-positive, and phrasing substitutions.
/// </summary>
public sealed class ContentSafetyOptions
{
    public const string SectionName = "ContentSafety";

    /// <summary>Words that must not appear in any script (case-insensitive exact match).</summary>
    public List<string> BannedWords { get; set; } = [];

    /// <summary>
    ///     Place-name / phrase exceptions masked out before the banned-word scan, so a banned
    ///     substring inside a legitimate suburb or road name is not a false positive. The
    ///     immutable engineered baseline; repository terms supply the rest.
    /// </summary>
    public List<string> AllowedPhrases { get; set; } = [];

    /// <summary>Phrasing substitutions applied before checking (case-insensitive).</summary>
    public Dictionary<string, string> Substitutions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
