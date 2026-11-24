using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Notifications;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Notifications;

public sealed class TeamsCardBuilderTests
{
    private const string JobUrl = "https://ops.example.com/jobs/test-1";

    // ── Helpers ────────────────────────────────────────────────────────

    private static Job CreateTestJob(
        JobStatus status = JobStatus.WaitingApproval,
        Option<DateTime> scheduledForUtc = default)
    {
        var job = Job.Create("test-1", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.SetStatus(status);
        if (scheduledForUtc.HasValue)
            job.ScheduledForUtc = scheduledForUtc;
        return job;
    }

    // ── Suppressed ────────────────────────────────────────────────────

    [Fact]
    public void Suppressed_ReturnsNull()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.Suppressed, CreateTestJob(), JobUrl);
        card.ShouldBeNull();
    }

    // ── Container style per tier ──────────────────────────────────────

    [Fact]
    public void ReadyForReview_ContainerIsEmphasis()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        var container = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        container["style"].ShouldBe("emphasis");
    }

    [Fact]
    public void ApprovalReminder_ContainerIsEmphasis()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ApprovalReminder, CreateTestJob(), JobUrl)!;
        var container = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        container["style"].ShouldBe("emphasis");
    }

    [Fact]
    public void UrgentApproval_ContainerIsAttention()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.UrgentApproval, CreateTestJob(), JobUrl)!;
        var container = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        container["style"].ShouldBe("attention");
    }

    [Fact]
    public void FailedNotCovered_FallbackUsed_ContainerIsAttention()
    {
        var job = CreateTestJob(JobStatus.FallbackUsed);
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, job, JobUrl)!;
        var container = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        container["style"].ShouldBe("attention");
    }

    [Fact]
    public void FailedNotCovered_Failed_ContainerIsAttention()
    {
        var job = CreateTestJob(JobStatus.Failed);
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, job, JobUrl)!;
        var container = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        container["style"].ShouldBe("attention");
    }

    // ── Action buttons (present vs absent) ────────────────────────────

    [Fact]
    public void ReadyForReview_HasReviewAction()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        var actions = (object[])card["actions"]!;
        var action = (Dictionary<string, object?>)actions[0];
        action["title"].ShouldBe("Review");
        action["url"].ShouldBe(JobUrl);
    }

    [Fact]
    public void ApprovalReminder_HasReviewAction()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ApprovalReminder, CreateTestJob(), JobUrl)!;
        var actions = (object[])card["actions"]!;
        var action = (Dictionary<string, object?>)actions[0];
        action["title"].ShouldBe("Review");
    }

    [Fact]
    public void UrgentApproval_HasApproveNowAction()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.UrgentApproval, CreateTestJob(), JobUrl)!;
        var actions = (object[])card["actions"]!;
        var action = (Dictionary<string, object?>)actions[0];
        action["title"].ShouldBe("Approve now");
    }

    [Fact]
    public void FailedNotCovered_HasOpenConsoleAction()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, CreateTestJob(JobStatus.Failed), JobUrl)!;
        var actions = (object[])card["actions"]!;
        var action = (Dictionary<string, object?>)actions[0];
        action["title"].ShouldBe("Open console");
    }

    [Fact]
    public void FailedCovered_NoActions()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedCovered, CreateTestJob(), JobUrl)!;
        card.ContainsKey("actions").ShouldBeFalse();
    }

    [Fact]
    public void MissedDeadlineClosure_NoActions()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.MissedDeadlineClosure, CreateTestJob(), JobUrl)!;
        card.ContainsKey("actions").ShouldBeFalse();
    }

    // ── msteams width Full ────────────────────────────────────────────

    [Fact]
    public void ReadyForReview_HasMsteamsWidthFull()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        var msteams = (Dictionary<string, object?>)card["msteams"]!;
        msteams["width"].ShouldBe("Full");
    }

    [Fact]
    public void ApprovalReminder_HasMsteamsWidthFull()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ApprovalReminder, CreateTestJob(), JobUrl)!;
        var msteams = (Dictionary<string, object?>)card["msteams"]!;
        msteams["width"].ShouldBe("Full");
    }

    [Fact]
    public void UrgentApproval_HasMsteamsWidthFull()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.UrgentApproval, CreateTestJob(), JobUrl)!;
        var msteams = (Dictionary<string, object?>)card["msteams"]!;
        msteams["width"].ShouldBe("Full");
    }

    [Fact]
    public void FailedNotCovered_HasMsteamsWidthFull()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, CreateTestJob(JobStatus.Failed), JobUrl)!;
        var msteams = (Dictionary<string, object?>)card["msteams"]!;
        msteams["width"].ShouldBe("Full");
    }

    [Fact]
    public void FailedCovered_NoMsteamsKey()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedCovered, CreateTestJob(), JobUrl)!;
        card.ContainsKey("msteams").ShouldBeFalse();
    }

    [Fact]
    public void MissedDeadlineClosure_NoMsteamsKey()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.MissedDeadlineClosure, CreateTestJob(), JobUrl)!;
        card.ContainsKey("msteams").ShouldBeFalse();
    }

    // ── Headlines / text content ──────────────────────────────────────

    [Fact]
    public void ReadyForReview_Scheduled_HeadlineEmbedsTime()
    {
        var scheduled = new DateTime(2025, 7, 5, 6, 30, 0, DateTimeKind.Utc);
        var job = CreateTestJob(scheduledForUtc: Option<DateTime>.Some(scheduled));
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, job, JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("{{TIME(2025-07-05T06:30:00Z)}}");
    }

    [Fact]
    public void ReadyForReview_Unscheduled_SaysReviewNow()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("review now");
    }

    [Fact]
    public void ApprovalReminder_Scheduled_HeadlineEmbedsTime()
    {
        var scheduled = new DateTime(2025, 7, 5, 8, 0, 0, DateTimeKind.Utc);
        var job = CreateTestJob(scheduledForUtc: Option<DateTime>.Some(scheduled));
        var card = TeamsCardBuilder.Build(NotificationKind.ApprovalReminder, job, JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("{{TIME(2025-07-05T08:00:00Z)}}");
    }

    [Fact]
    public void ApprovalReminder_EndsWithReminderMarker()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ApprovalReminder, CreateTestJob(), JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldEndWith("· reminder");
    }

    [Fact]
    public void ReadyForReview_DoesNotEndWithReminderMarker()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldNotContain("· reminder");
    }

    [Fact]
    public void UrgentApproval_Unscheduled_SaysApproveStationNow()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.UrgentApproval, CreateTestJob(), JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("Approve 2GB now");
    }

    [Fact]
    public void UrgentApproval_Scheduled_HeadlineEmbedsTime()
    {
        var scheduled = new DateTime(2025, 7, 5, 6, 45, 0, DateTimeKind.Utc);
        var job = CreateTestJob(scheduledForUtc: Option<DateTime>.Some(scheduled));
        var card = TeamsCardBuilder.Build(NotificationKind.UrgentApproval, job, JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("{{TIME(2025-07-05T06:45:00Z)}}");
    }

    [Fact]
    public void FailedNotCovered_FallbackUsed_FillerWillAirHeadline()
    {
        var job = CreateTestJob(JobStatus.FallbackUsed);
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, job, JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("filler will air unless you act");
    }

    [Fact]
    public void FailedNotCovered_Failed_NothingQueuedHeadline()
    {
        var job = CreateTestJob(JobStatus.Failed);
        var card = TeamsCardBuilder.Build(NotificationKind.FailedNotCovered, job, JobUrl)!;
        var text = FirstHeadlineText(card);
        text.ShouldContain("nothing queued to air");
    }

    [Fact]
    public void FailedCovered_ShowsStationAndCoveredMessage()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedCovered, CreateTestJob(), JobUrl)!;
        var columnSet = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        columnSet["type"].ShouldBe("ColumnSet");

        // The text column should contain the station and "covered" message
        var columns = (object[])columnSet["columns"]!;
        var textColumn = (Dictionary<string, object?>)columns[1];
        var items = (object[])textColumn["items"]!;
        var textBlock = (Dictionary<string, object?>)items[0];
        ((string)textBlock["text"]!).ShouldContain("2GB");
        ((string)textBlock["text"]!).ShouldContain("covered");
    }

    [Fact]
    public void MissedDeadlineClosure_ShowsStationAndClosureMessage()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.MissedDeadlineClosure, CreateTestJob(), JobUrl)!;
        var columnSet = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        columnSet["type"].ShouldBe("ColumnSet");

        var columns = (object[])columnSet["columns"]!;
        var textColumn = (Dictionary<string, object?>)columns[1];
        var items = (object[])textColumn["items"]!;
        var textBlock = (Dictionary<string, object?>)items[0];
        ((string)textBlock["text"]!).ShouldContain("2GB");
        ((string)textBlock["text"]!).ShouldContain("not reviewed");
    }

    // ── Quiet tier check mark ─────────────────────────────────────────

    [Fact]
    public void FailedCovered_HasCheckmarkColumn()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedCovered, CreateTestJob(), JobUrl)!;
        var columnSet = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        var columns = (object[])columnSet["columns"]!;
        var checkColumn = (Dictionary<string, object?>)columns[0];
        var items = (object[])checkColumn["items"]!;
        var checkText = (Dictionary<string, object?>)items[0];
        ((string)checkText["text"]!).ShouldBe("✓");
    }

    [Fact]
    public void MissedDeadlineClosure_HasCheckmarkColumn()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.MissedDeadlineClosure, CreateTestJob(), JobUrl)!;
        var columnSet = (Dictionary<string, object?>)((object[])card["body"]!)[0];
        var columns = (object[])columnSet["columns"]!;
        var checkColumn = (Dictionary<string, object?>)columns[0];
        var items = (object[])checkColumn["items"]!;
        var checkText = (Dictionary<string, object?>)items[0];
        ((string)checkText["text"]!).ShouldBe("✓");
    }

    // ── Serialization: $schema and msteams survive ────────────────────

    [Fact]
    public void Serialize_ReadyForReview_SchemaAndMsteamsKeysSurvive()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.ReadyForReview, CreateTestJob(), JobUrl)!;
        string json = JsonSerializer.Serialize(card, JsonDefaults.Notification);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("$schema", out var schema).ShouldBeTrue();
        schema.GetString()!.ShouldContain("adaptivecards.io");

        root.TryGetProperty("msteams", out var msteams).ShouldBeTrue();
        msteams.TryGetProperty("width", out var width).ShouldBeTrue();
        width.GetString().ShouldBe("Full");
    }

    [Fact]
    public void Serialize_QuietTier_NoActionsKey()
    {
        var card = TeamsCardBuilder.Build(NotificationKind.FailedCovered, CreateTestJob(), JobUrl)!;
        string json = JsonSerializer.Serialize(card, JsonDefaults.Notification);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("actions", out _).ShouldBeFalse();
        root.TryGetProperty("msteams", out _).ShouldBeFalse();
    }

    // ── Private helpers ───────────────────────────────────────────────

    private static string FirstHeadlineText(Dictionary<string, object?> card)
    {
        // Action-tier cards: body[0] is a Container whose items[0] is a TextBlock
        // Quiet-tier cards: body[0] is a ColumnSet — extract from the stretch column
        var body = (object[])card["body"]!;
        var first = (Dictionary<string, object?>)body[0];

        if ((string?)first["type"] == "Container")
        {
            var items = (object[])first["items"]!;
            var textBlock = (Dictionary<string, object?>)items[0];
            return (string)textBlock["text"]!;
        }

        // ColumnSet: text is in columns[1] items[0]
        var columns = (object[])first["columns"]!;
        var textColumn = (Dictionary<string, object?>)columns[1];
        var textItems = (object[])textColumn["items"]!;
        var textBlockFromColumn = (Dictionary<string, object?>)textItems[0];
        return (string)textBlockFromColumn["text"]!;
    }
}
