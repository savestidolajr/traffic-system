using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public sealed class QueuePublisherTests
{
    private const string BroadcastQueue = QueueNames.Broadcast;
    private const string RegenerationQueue = QueueNames.Regeneration;

    private static (QueuePublisher Publisher, QueueClient BroadcastClient, QueueClient RegenerationClient) CreateSut()
    {
        var broadcastClient = Substitute.For<QueueClient>();
        var regenClient = Substitute.For<QueueClient>();

        var serviceClient = Substitute.For<QueueServiceClient>();
        serviceClient.GetQueueClient(BroadcastQueue).Returns(broadcastClient);
        serviceClient.GetQueueClient(RegenerationQueue).Returns(regenClient);

        var publisher = new QueuePublisher(serviceClient, NullLogger<QueuePublisher>.Instance);
        return (publisher, broadcastClient, regenClient);
    }

    // ── PublishBroadcastMessageAsync ─────────────────────────────────

    [Fact]
    public async Task PublishBroadcastMessage_Success()
    {
        var (sut, broadcastClient, _) = CreateSut();
        var msg = new BroadcastQueueMessage("job-1", "SYD", "Sydney", 30, -33.8, 151.2, 10.0, "voice-1");

        var result = await sut.PublishBroadcastMessageAsync(msg);

        result.IsSuccess.ShouldBeTrue();
        await broadcastClient.Received(1).CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>());
        await broadcastClient.Received(1).SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBroadcastMessage_SerializesCorrectly()
    {
        var (sut, broadcastClient, _) = CreateSut();
        var msg = new BroadcastQueueMessage("job-2", "MEL", "Melbourne", 25, -37.8, 144.9, 15.0);
        string? capturedJson = null;
        await broadcastClient.SendMessageAsync(
            Arg.Do<string>(s => capturedJson = s),
            Arg.Any<CancellationToken>());

        await sut.PublishBroadcastMessageAsync(msg);

        capturedJson.ShouldNotBeNull();
        capturedJson.ShouldContain("job-2");
        capturedJson.ShouldContain("Melbourne");
    }

    [Fact]
    public async Task PublishBroadcastMessage_Fails_OnException()
    {
        var (sut, broadcastClient, _) = CreateSut();
        broadcastClient.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException("Queue unavailable"));

        var result = await sut.PublishBroadcastMessageAsync(
            new BroadcastQueueMessage("j", "M", "L", 30, 0, 0, 0));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Queue unavailable");
    }

    // ── PublishRegenerationMessageAsync ──────────────────────────────

    [Fact]
    public async Task PublishRegenerationMessage_Success()
    {
        var (sut, _, regenClient) = CreateSut();
        var msg = new RegenerationQueueMessage("job-3", "Sydney", 30, "traffic data", []);

        var result = await sut.PublishRegenerationMessageAsync(msg);

        result.IsSuccess.ShouldBeTrue();
        await regenClient.Received(1).SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRegenerationMessage_Fails_OnException()
    {
        var (sut, _, regenClient) = CreateSut();
        regenClient.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException("Storage error"));

        var result = await sut.PublishRegenerationMessageAsync(
            new RegenerationQueueMessage("j", "L", 30, "t", []));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Storage error");
    }

    [Fact]
    public async Task PublishRegenerationMessage_UsesCorrectQueue()
    {
        var (sut, broadcastClient, regenClient) = CreateSut();
        var msg = new RegenerationQueueMessage("job-4", "Loc", 30, "traffic", []);

        await sut.PublishRegenerationMessageAsync(msg);

        await regenClient.Received(1).CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>());
        await broadcastClient.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
