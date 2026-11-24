namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Severity of an operator-facing pipeline alert notification (e.g. Teams webhook), controlling icon and log level.</summary>
public enum AlertSeverity
{
    Warning,
    Critical
}
