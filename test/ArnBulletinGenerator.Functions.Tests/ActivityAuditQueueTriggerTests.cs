using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Functions.Triggers.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ActivityAuditQueueTriggerTests
{
    [Fact]
    public async Task Run_RedeliveryRecordsTheSameActivityEntry()
    {
        var activityLog = Substitute.For<IActivityLogRepository>();
        activityLog.RecordAsync(
                Arg.Any<Domain.Models.ActivityLogEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var trigger = new ActivityAuditQueueTrigger(
            activityLog,
            Options.Create(new AuditQueueOptions { Enabled = true }),
            Substitute.For<ILogger<ActivityAuditQueueTrigger>>());
        var message = new ActivityAuditMessage(
            new DateTime(2026, 9, 2, 1, 2, 3, DateTimeKind.Utc),
            "Jamie Operator", "op-123", "PUT", "/api/schedules/adelaide", "schedules",
            "adelaide", 200, "{\"enabled\":true}", "request-123");
        string json = JsonSerializer.Serialize(message);

        await trigger.ProcessAsync(json, CancellationToken.None);
        await trigger.ProcessAsync(json, CancellationToken.None);

        await activityLog.Received(2).RecordAsync(
            Arg.Is<Domain.Models.ActivityLogEntry>(entry =>
                entry.AtUtc == message.AtUtc &&
                entry.ActorName == message.ActorName &&
                entry.ActorObjectId == message.ActorObjectId &&
                entry.Method == message.Method &&
                entry.Path == message.Path &&
                entry.Resource == message.Resource &&
                entry.EntityId == message.EntityId &&
                entry.StatusCode == message.StatusCode &&
                entry.RequestBodyJson == message.RequestBodyJson),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_DisabledQueueDoesNotRecord()
    {
        var activityLog = Substitute.For<IActivityLogRepository>();
        var trigger = new ActivityAuditQueueTrigger(
            activityLog,
            Options.Create(new AuditQueueOptions()),
            Substitute.For<ILogger<ActivityAuditQueueTrigger>>());
        var json = JsonSerializer.Serialize(new ActivityAuditMessage(
            DateTime.UtcNow, "actor", "object", "PUT", "/api/schedules/adelaide", "schedules",
            "adelaide", 200, null, "request-123"));

        await trigger.ProcessAsync(json, CancellationToken.None);

        await activityLog.DidNotReceive().RecordAsync(
            Arg.Any<Domain.Models.ActivityLogEntry>(), Arg.Any<CancellationToken>());
    }
}
