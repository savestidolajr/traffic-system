using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Notifications;

/// <summary>
///     Builds the Adaptive Card JSON (as serializable dictionaries) for each
///     <see cref="NotificationKind" /> tier. Design decided 2026-07-05 via the
///     four-critic review; the system's severity grammar:
///     grey emphasis = review, red attention = act now, bare small text + ✓ = handled.
///     Platform constraints honoured here (verified against Teams renderer):
///     Adaptive Cards 1.4, Action.OpenUrl only (style:"destructive" is silently
///     ignored by Teams so it is never emitted), msteams.width Full on action
///     tiers, {{TIME(rfc3339)}} for viewer-local times, leading emoji retained as
///     the severity carrier in Teams' collapsed message preview.
/// </summary>
public static class TeamsCardBuilder
{
    private const string SchemaUrl = "http://adaptivecards.io/schemas/adaptive-card.json";

    /// <summary>Builds the card for the given kind, or null for suppressed kinds.</summary>
    public static Dictionary<string, object?>? Build(NotificationKind kind, Job job, string jobUrl)
    {
        string station = job.Station;
        // {{TIME(rfc3339)}} renders in each viewer's local timezone; null for manual jobs.
        string? time = job.ScheduledForUtc.Match(
            dt => (string?)$"{{{{TIME({dt:yyyy-MM-dd'T'HH:mm:ss'Z'})}}}}",
            () => null);
        string slot = time is null ? "" : $" {time}";

        return kind switch
        {
            NotificationKind.ReadyForReview => Ready(station, time, jobUrl, reminder: false),
            NotificationKind.ApprovalReminder => Ready(station, time, jobUrl, reminder: true),
            NotificationKind.UrgentApproval => Urgent(station, time, jobUrl),
            NotificationKind.FailedNotCovered => Intervention(station, slot, jobUrl, fillerQueued: job.Status == JobStatus.FallbackUsed),
            NotificationKind.FailedCovered => Quiet($"**{station}{slot} failed** — covered, the previous bulletin re-airs", jobUrl),
            NotificationKind.MissedDeadlineClosure => Quiet($"**{station}{slot}** — previous bulletin re-aired (not reviewed)", jobUrl),
            _ => null
        };
    }

    // ── Tier templates ──────────────────────────────────────────────

    private static Dictionary<string, object?> Ready(string station, string? time, string url, bool reminder)
    {
        string deadline = time is null ? "review now" : $"review by {time}";
        string headline = $"📋 **{station}** — {deadline}{(reminder ? " · reminder" : "")}";
        return Card(fullWidth: true,
            body:
            [
                ColoredContainer("emphasis",
                    Text(headline, weight: "Bolder", size: "Medium"),
                    Text("Falls back to the previous bulletin if missed.", size: "Small", subtle: true, spacing: "None"))
            ],
            action: OpenUrl("Review", url));
    }

    private static Dictionary<string, object?> Urgent(string station, string? time, string url)
    {
        string verb = time is null ? $"Approve {station} now" : $"Approve {station} by {time}";
        return Card(fullWidth: true,
            body:
            [
                ColoredContainer("attention",
                    Text($"🔴 **{verb}** — or the previous bulletin airs again",
                        weight: "Bolder", size: "Medium", color: "Attention"),
                    Text("Minutes to air · stale traffic will loop.", size: "Small", subtle: true, spacing: "None"))
            ],
            action: OpenUrl("Approve now", url));
    }

    private static Dictionary<string, object?> Intervention(string station, string slot, string url, bool fillerQueued)
    {
        string headline = fillerQueued
            ? $"🔴 **{station}{slot} — filler will air unless you act**"
            : $"🔴 **{station}{slot} — bulletin failed, nothing queued to air**";
        string guidance = fillerQueued
            ? "No same-day bulletin available. Record a manual read or approve an earlier one."
            : "Record a manual read or approve an earlier bulletin.";
        return Card(fullWidth: true,
            body:
            [
                ColoredContainer("attention",
                    Text(headline, weight: "Bolder", size: "Medium", color: "Attention"),
                    Text(guidance, size: "Small", spacing: "None"))
            ],
            action: OpenUrl("Open console", url));
    }

    private static Dictionary<string, object?> Quiet(string message, string url)
    {
        return Card(fullWidth: false,
            body:
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "ColumnSet",
                    ["columns"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "Column",
                            ["width"] = "auto",
                            ["items"] = new object[] { Text("✓", weight: "Bolder", color: "Good", spacing: "None") },
                            ["verticalContentAlignment"] = "Center"
                        },
                        new Dictionary<string, object?>
                        {
                            ["type"] = "Column",
                            ["width"] = "stretch",
                            ["items"] = new object[] { Text($"{message} · [details]({url})", size: "Small", spacing: "None") },
                            ["verticalContentAlignment"] = "Center"
                        }
                    }
                }
            ],
            action: null);
    }

    // ── Element helpers ─────────────────────────────────────────────

    private static Dictionary<string, object?> Card(bool fullWidth, object[] body, Dictionary<string, object?>? action)
    {
        var card = new Dictionary<string, object?>
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = SchemaUrl,
            ["version"] = "1.4",
            ["body"] = body
        };
        if (fullWidth)
            card["msteams"] = new Dictionary<string, object?> { ["width"] = "Full" };
        if (action is not null)
            card["actions"] = new object[] { action };
        return card;
    }

    private static Dictionary<string, object?> ColoredContainer(string style, params object[] items)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "Container",
            ["style"] = style,
            ["bleed"] = true,
            ["items"] = items
        };
    }

    private static Dictionary<string, object?> Text(
        string text, string? weight = null, string? size = null, string? color = null,
        bool subtle = false, string? spacing = null)
    {
        var block = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["wrap"] = true
        };
        if (weight is not null) block["weight"] = weight;
        if (size is not null) block["size"] = size;
        if (color is not null) block["color"] = color;
        if (subtle) block["isSubtle"] = true;
        if (spacing is not null) block["spacing"] = spacing;
        return block;
    }

    private static Dictionary<string, object?> OpenUrl(string title, string url)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "Action.OpenUrl",
            ["title"] = title,
            ["url"] = url
        };
    }
}
