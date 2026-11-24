using System.Globalization;
using System.Text;

namespace ArnBulletinGenerator.Application.Validation;

/// <summary>
///     Rejects Unicode control and format characters in operator-authored SINGLE-LINE names -
///     road names, a schedule's name and station. These fields are echoed back into the console
///     and interpolated into the generation prompt, so a bidirectional override (U+202E, accepted
///     and stored on dev 2026-07-28) lets a stored name render as something else entirely, and an
///     embedded newline lets operator config impersonate prompt structure.
///     <para>
///         Deliberately NOT applied to free-text bodies (filler statements, guidance, style rules):
///         those are legitimately multi-line. This is a name guard, not a prose guard.
///     </para>
/// </summary>
public static class OperatorTextGuard
{
    /// <summary>Shared refusal wording so every call site reports the same thing.</summary>
    public const string RejectionMessage =
        "must not contain control or formatting characters (including line breaks)";

    /// <summary>
    ///     True when <paramref name="value" /> contains any Unicode control, format, separator,
    ///     private-use or unpaired-surrogate character. Enumerates runes rather than chars so a
    ///     character outside the BMP is judged once, on its real category.
    /// </summary>
    public static bool HasControlOrFormatCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.Surrogate)
            {
                return true;
            }
        }

        return false;
    }
}
