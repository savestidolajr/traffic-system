namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A single timestamped diagnostic entry appended to a Job's pipeline log for operator troubleshooting.</summary>
public sealed record JobLog(
    DateTime At,
    JobLogLevel Level,
    string Message);
