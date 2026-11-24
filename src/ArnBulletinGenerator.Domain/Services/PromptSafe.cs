using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Prompt-injection hardening for any text that originates outside the engineered
///     prompt core — admin/operator guidance, producer feedback, and provider data
///     (HERE incident free-text). Strips the structural delimiter tokens the prompt uses
///     to separate trusted instructions from untrusted content, so an author or a data
///     field cannot close a block early and escape into instruction space.
/// </summary>
public static partial class PromptSafe
{
    /// <summary>Opening/closing tags the prompt reserves to fence injected blocks.</summary>
    [GeneratedRegex(@"</?\s*(guidance|data|producer-feedback|house-style|operator-notes)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelimiterTokens();

    [GeneratedRegex(@"[ \t]*\r?\n[ \t]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CollapsibleWhitespace();

    /// <summary>
    ///     Removes reserved delimiter tokens but preserves the text's own line breaks —
    ///     for multi-line blocks like the traffic data narrative.
    /// </summary>
    public static string StripDelimiters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return DelimiterTokens().Replace(text, " ").Trim();
    }

    /// <summary>
    ///     Strips delimiter tokens AND collapses all whitespace to single spaces — for
    ///     short, single-line directives where embedded newlines would only be noise.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        string cleaned = DelimiterTokens().Replace(text, " ");
        cleaned = LineBreaks().Replace(cleaned, " ");
        cleaned = CollapsibleWhitespace().Replace(cleaned, " ");
        return cleaned.Trim();
    }

    /// <summary>
    ///     Wraps provider/data content in a <c>&lt;data&gt;</c> envelope after stripping any
    ///     reserved delimiter tokens from the content. The system prompt instructs the model
    ///     to treat everything inside <c>&lt;data&gt;</c> as information to report, never as
    ///     instructions — the indirect-injection defense for HERE incident free-text.
    /// </summary>
    public static string WrapData(string? content)
    {
        return $"<data>\n{StripDelimiters(content)}\n</data>";
    }
}
