using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Notifications;

/// <summary>
///     Sends structured notifications to a webhook endpoint (Power Automate Workflows) via HTTP POST.
///     The webhook renders them as rich messages in Microsoft Teams.
/// </summary>
public sealed class WebhookNotificationService(
    IOptions<WebhookOptions> options,
    HttpClient httpClient,
    ILogger<WebhookNotificationService> logger) : INotificationService
{
    private const string SchemaVersion = "3.0";

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Notification;

    private readonly WebhookOptions _options = options.Value;

    public async Task<Result> SendJobNotificationAsync(Job job, NotificationKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = kind ?? DefaultKind(job.Status);
        if (resolved == NotificationKind.Suppressed)
        {
            logger.LogDebug("Notification suppressed for job {JobId} ({Status})", job.JobId, job.Status);
            return Result.Success();
        }

        if (!IsEnabled("job", job.JobId)) return Result.Success();

        logger.LogInformation(
            "Sending {Kind} notification for job {JobId} ({Market}/{Station})",
            resolved, job.JobId, job.Market, job.Station);

        object payload = BuildJobPayload(job, resolved);
        return await PostAsync(payload, cancellationToken);
    }

    /// <summary>
    ///     Status-derived tier for callers that don't pass one. Approved/Published/Rejected
    ///     and routine fallbacks are audit noise (card design decision 2026-07-05) — the
    ///     callers that DO warrant fallback cards pass an explicit kind.
    /// </summary>
    private static NotificationKind DefaultKind(JobStatus status)
    {
        return status switch
        {
            JobStatus.WaitingApproval => NotificationKind.ReadyForReview,
            JobStatus.Failed => NotificationKind.FailedNotCovered,
            // Missed is silent hygiene, not an event: nobody acted and nothing aired, so a
            // card would only add noise. Explicit arm so the silence survives future edits.
            JobStatus.Missed => NotificationKind.Suppressed,
            _ => NotificationKind.Suppressed
        };
    }

    public async Task<Result> SendAlertNotificationAsync(
        string market, string station, string alertMessage, AlertSeverity severity,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled("alert", market)) return Result.Success();

        logger.LogInformation("Sending {Severity} alert for {Market}/{Station}", severity, market, station);

        var payload = new
        {
            title = $"{SeverityIcon(severity)} {severity} Alert — {market}",
            jobId = string.Empty,
            market,
            station,
            scheduledAtDisplay = string.Empty,
            status = severity.ToString(),
            previewText = alertMessage,
            createdBy = "ArnBulletinGenerator",
            createdAt = DateTime.UtcNow.ToString("o"),
            schemaVersion = SchemaVersion,
            correlationId = Guid.NewGuid().ToString("N")[..8],
            allowedActions = Array.Empty<string>(),
            url = BuildMarketUrl(market)
        };

        return await PostAsync(payload, cancellationToken);
    }

    // ── Payload Builders ────────────────────────────────────────────

    private object BuildJobPayload(Job job, NotificationKind kind)
    {
        string url = BuildJobUrl(job.JobId);
        return new
        {
            card = TeamsCardBuilder.Build(kind, job, url),
            kind = kind.ToString(),
            title = StatusTitle(job.Status),
            jobId = job.JobId,
            market = job.Market,
            station = job.Station,
            scheduledAtDisplay = job.ScheduledForUtc.Match(
                dt => FormatUtcTimestamp(dt),
                () => "Not scheduled"),
            status = FormatStatus(job.Status),
            previewText = BuildPreviewText(job),
            createdBy = "ArnBulletinGenerator",
            createdAt = job.CreatedAt.ToString("o"),
            schemaVersion = SchemaVersion,
            correlationId = job.JobId,
            allowedActions = AllowedActions(job.Status),
            url
        };
    }

    private static string BuildPreviewText(Job job)
    {
        var parts = new List<string>();

        // Duration info
        double targetSec = job.TargetDurationSeconds;
        string measuredDisplay = job.MeasuredDurationSec.Match(
            d => $"{d:F1}s",
            () => "pending");
        parts.Add($"Duration: {measuredDisplay} / {targetSec:F0}s target");

        // Attempt info
        if (job.AttemptCount > 1)
            parts.Add($"Attempt #{job.AttemptCount}");

        // Incident count
        if (job.Incidents.Count > 0)
            parts.Add($"{job.Incidents.Count} traffic incident{(job.Incidents.Count != 1 ? "s" : "")}");

        // Script preview
        job.ScriptText.Tap(script =>
        {
            string preview = Truncate(script.ReplaceLineEndings(" "), 300);
            parts.Add($"\n{preview}");
        });

        // Rejection info (for rejected jobs)
        if (job.Status == JobStatus.Rejected && job.RejectionHistory.Count > 0)
        {
            var latest = job.RejectionHistory[^1];
            parts.Add($"\nRejection reason: {latest.Reason}");
            if (!string.IsNullOrWhiteSpace(latest.Notes))
                parts.Add($"Notes: {latest.Notes}");
        }

        // Error info (for failed jobs)
        job.ErrorMessage.Tap(err => parts.Add($"\nError: {Truncate(err, 200)}"));

        return string.Join(" • ", parts.Where(p => !p.StartsWith('\n')))
               + string.Concat(parts.Where(p => p.StartsWith('\n')));
    }

    // ── Status Mapping ──────────────────────────────────────────────

    private static string StatusTitle(JobStatus status)
    {
        return status switch
        {
            JobStatus.WaitingApproval => "🎙️ Bulletin Ready for Review",
            JobStatus.Approved => "✅ Bulletin Approved",
            JobStatus.Rejected => "❌ Bulletin Rejected",
            JobStatus.Published => "📡 Bulletin Published",
            JobStatus.Failed => "🚨 Bulletin Failed",
            JobStatus.FallbackUsed => "🔄 Fallback Bulletin Used",
            // Cold and settled: the slot aired nothing and nobody decided. Never frame it as
            // a live "Bulletin Update".
            JobStatus.Missed => "⏰ Bulletin Missed",
            _ => $"📋 Bulletin Update — {FormatStatus(status)}"
        };
    }

    private static string[] AllowedActions(JobStatus status)
    {
        return status switch
        {
            JobStatus.WaitingApproval => ["Approve", "Reject"],
            JobStatus.Approved => ["Publish"],
            JobStatus.Rejected => ["Regenerate"],
            JobStatus.FallbackUsed => ["Approve"],
            _ => []
        };
    }

    private static string FormatStatus(JobStatus status)
    {
        return status switch
        {
            JobStatus.WaitingApproval => "Waiting Approval",
            JobStatus.HereFetched => "Traffic Fetched",
            JobStatus.IncidentsSelected => "Incidents Selected",
            JobStatus.AwaitingCuration => "Awaiting Curation",
            JobStatus.ScriptReady => "Script Ready",
            JobStatus.SynthesizingAudio => "Synthesizing Audio",
            JobStatus.AudioReady => "Audio Ready",
            JobStatus.FallbackUsed => "Fallback Used",
            _ => status.ToString()
        };
    }

    private static string SeverityIcon(AlertSeverity severity)
    {
        return severity switch
        {
            AlertSeverity.Critical => "🚨",
            AlertSeverity.Warning => "⚠️",
            _ => "ℹ️"
        };
    }

    // ── HTTP ────────────────────────────────────────────────────────

    private async Task<Result> PostAsync(object payload, CancellationToken cancellationToken)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await httpClient.PostAsync(_options.EndpointUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Webhook notification sent successfully");
                return Result.Success();
            }

            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Webhook notification failed with {StatusCode}: {Error}", response.StatusCode, error);
            return Result.Failure($"Webhook call failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send webhook notification");
            return Result.Failure($"Webhook call failed: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private bool IsEnabled(string notificationType, string context)
    {
        if (_options.Enabled) return true;

        logger.LogInformation("Notifications disabled — skipping {Type} for {Context}", notificationType, context);
        return false;
    }

    private string BuildJobUrl(string jobId)
    {
        string? baseUrl = _options.OpsConsoleBaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : $"{baseUrl}/jobs/{jobId}";
    }

    private string BuildMarketUrl(string _)
    {
        string? baseUrl = _options.OpsConsoleBaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : $"{baseUrl}/jobs";
    }

    private static string FormatUtcTimestamp(DateTime utc)
    {
        return utc.ToString("ddd dd MMM yyyy HH:mm 'UTC'");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
    }
}
