using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>Validation, identity, expiry, and capacity policy for future lessons.</summary>
public static class FutureLessonPolicy
{
    public const int MaxActivePerStation = 8;
    public const int MinTextLength = 20;
    public const int MaxTextLength = 300;

    /// <summary>Hard per-station cap on stored lessons (candidates + active) so writer/LLM context stays bounded.</summary>
    public const int MaxStoredPerStation = 20;

    /// <summary>Independently published reinforcements before an inferred candidate auto-activates.</summary>
    public const int ActivationOccurrences = 3;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    public static Result Validate(FutureLessonCategory category, string? text)
    {
        if (!Enum.IsDefined(category))
            return Result.Failure("Future lesson category is invalid.");
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure("Future lesson text is required.");

        int length = text.Trim().Length;
        if (length < MinTextLength || length > MaxTextLength)
            return Result.Failure(
                $"Future lesson text must be {MinTextLength}-{MaxTextLength} characters.");

        return Result.Success();
    }

    public static string CreateId(string station, FutureLessonCategory category, string text)
    {
        string normalizedText = string.Join(' ',
            text.Trim().ToLowerInvariant().Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string identity = $"{MarketKey.Normalize(station)}|{category}|{normalizedText}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
