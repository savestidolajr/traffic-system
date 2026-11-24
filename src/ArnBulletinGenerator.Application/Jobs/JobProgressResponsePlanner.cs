using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>HTTP outcomes for a point-read progress request.</summary>
public enum JobProgressResponseKind
{
    NotFound,
    NotModified,
    Ok
}

/// <summary>Pure result of applying the progress document and conditional request rules.</summary>
public sealed record JobProgressResponsePlan(JobProgressResponseKind Kind, string? ETag);

/// <summary>
///     Plans the progress endpoint's 200, 304 and 404 decision without HTTP dependencies. The version
///     is the stable application-level cache token because a progress document increments it once per
///     appended event.
/// </summary>
public static class JobProgressResponsePlanner
{
    /// <summary>Returns the response kind and quoted ETag for the supplied document.</summary>
    public static JobProgressResponsePlan Plan(
        JobProgressDocument? document,
        string? ifNoneMatch)
    {
        if (document is null)
            return new JobProgressResponsePlan(JobProgressResponseKind.NotFound, null);

        string etag = $"\"v{document.Version}\"";
        string? validator = ifNoneMatch?.Trim();
        if (validator?.StartsWith("W/", StringComparison.OrdinalIgnoreCase) == true)
            validator = validator[2..].Trim();

        return string.Equals(validator, etag, StringComparison.Ordinal)
            ? new JobProgressResponsePlan(JobProgressResponseKind.NotModified, etag)
            : new JobProgressResponsePlan(JobProgressResponseKind.Ok, etag);
    }
}
