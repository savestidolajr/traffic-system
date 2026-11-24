using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration options for webhook notifications (Power Automate Workflows).
///     Sends notifications via HTTP POST to a webhook endpoint.
///     Used for HITL approval notifications, escalation alerts, and failure notifications.
/// </summary>
public sealed class WebhookOptions : IValidatableObject
{
    public const string SectionName = "Webhook";

    /// <summary>
    ///     Whether webhook notifications are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Webhook URL for sending notifications via HTTP POST.
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    ///     Base URL for the operations console (used for generating action URLs in notifications).
    /// </summary>
    public string? OpsConsoleBaseUrl { get; set; }

    /// <summary>
    ///     Retry attempts after the first failed POST, applied by
    ///     <see cref="ArnBulletinGenerator.Infrastructure.Notifications.WebhookRetryHandler" /> on a
    ///     5xx, 408, 429, transport error or per-attempt timeout. 0 means a single attempt.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    ///     Timeout in seconds for each HTTP call to the webhook.
    /// </summary>
    [Range(5, 60)]
    public int TimeoutSeconds { get; set; } = 15;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && (string.IsNullOrWhiteSpace(EndpointUrl) ||
                        !Uri.IsWellFormedUriString(EndpointUrl, UriKind.Absolute)))
            yield return new ValidationResult(
                "Endpoint URL must be a valid absolute URL when webhook notifications are enabled.",
                [nameof(EndpointUrl)]);
    }
}
