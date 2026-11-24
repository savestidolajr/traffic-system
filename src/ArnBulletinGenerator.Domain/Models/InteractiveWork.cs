using System.Globalization;
using System.Text;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>The independent admission pools used by interactive work.</summary>
public enum InteractiveWorkPool
{
    Llm,
    Tts
}

/// <summary>
/// The complete identity of an interactive operation. Every value participates in the key so a
/// result from one operator, job revision, voice, model, dictionary, or style can never be reused
/// by another operation.
/// </summary>
public sealed record InteractiveWorkKey(
    string PrincipalId,
    string Operation,
    string Revision,
    string Input,
    string? Model = null,
    string? Settings = null,
    string? Dictionary = null,
    string? Style = null)
{
    public InteractiveWorkKey(
        string principalId,
        string operation,
        int revision,
        string input,
        string? model = null,
        string? settings = null,
        string? dictionary = null,
        string? style = null)
        : this(principalId, operation, revision.ToString(CultureInfo.InvariantCulture), input,
            model, settings, dictionary, style)
    {
    }

    /// <summary>Canonicalises identity fields while preserving script-input casing.</summary>
    public InteractiveWorkKey Normalize()
    {
        return this with
        {
            PrincipalId = NormalizeToken(PrincipalId, uppercase: true),
            Operation = NormalizeToken(Operation, uppercase: true),
            Revision = NormalizeToken(Revision, uppercase: false),
            Input = NormalizeInput(Input),
            Model = NormalizeToken(Model, uppercase: true),
            Settings = NormalizeToken(Settings, uppercase: false),
            Dictionary = NormalizeToken(Dictionary, uppercase: true),
            Style = NormalizeToken(Style, uppercase: true)
        };
    }

    private static string NormalizeToken(string? value, bool uppercase)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string normalized = CollapseWhitespace(value.Normalize(NormalizationForm.FormKC));
        return uppercase ? normalized.ToUpperInvariant() : normalized;
    }

    private static string NormalizeInput(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : CollapseWhitespace(value.Normalize(NormalizationForm.FormKC));

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
                builder.Append(' ');
            builder.Append(character);
            pendingSpace = false;
        }
        return builder.ToString();
    }
}

/// <summary>One interactive operation and its optional per-call cache/deadline overrides.</summary>
public sealed record InteractiveWorkRequest(
    InteractiveWorkPool Pool,
    InteractiveWorkKey Key,
    TimeSpan? Deadline = null,
    bool BypassCache = false)
{
    public InteractiveWorkRequest(
        InteractiveWorkKey key,
        InteractiveWorkPool pool = InteractiveWorkPool.Llm,
        TimeSpan? deadline = null,
        bool bypassCache = false)
        : this(pool, key, deadline, bypassCache)
    {
    }
}
