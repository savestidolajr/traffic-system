using ArnBulletinGenerator.Infrastructure.Fakes;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class FakeDeliveryFileWriterTests : IDisposable
{
    private readonly string _root;
    private readonly FakeExternalCallLog _log = new();
    private readonly FakeDeliveryFileWriter _sut;

    public FakeDeliveryFileWriterTests()
    {
        // Point the fake at a throwaway folder so the probe assertions observe only this test's
        // writes, never anything the E2E suite left in the shared temp default.
        _root = Path.Combine(Path.GetTempPath(), "arn-test-fake-writer-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["E2E:ZettaOutputPath"] = _root
            })
            .Build();
        _sut = new FakeDeliveryFileWriter(configuration, _log);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task GetPendingWriteTimeAsync_AfterWrite_ReportsTheRealWriteTime()
    {
        var write = await _sut.WriteAsync([1, 2, 3], "MEL_TRAIN_TRAFF.wav");
        write.IsSuccess.ShouldBeTrue();

        var probe = await _sut.GetPendingWriteTimeAsync("MEL_TRAIN_TRAFF.wav");

        probe.IsSuccess.ShouldBeTrue();
        probe.Value.IsSome.ShouldBeTrue();
        probe.Value.Value.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task GetPendingWriteTimeAsync_UnwrittenName_ReturnsNone()
    {
        var probe = await _sut.GetPendingWriteTimeAsync("NEVER_WRITTEN.wav");

        probe.IsSuccess.ShouldBeTrue();
        probe.Value.IsNone.ShouldBeTrue();
    }
}
