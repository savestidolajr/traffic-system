using ArnBulletinGenerator.Infrastructure.Output;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

/// <summary>
///     The mock drop stands in for a LIVE broadcast input, so it has to be a faithful
///     stand-in: same guard contract as the real writers, and a tray probe that answers
///     "is this file still awaiting pickup" the same way. A mock that always succeeded, or
///     always reported an empty tray, would let a delivery defect pass every non-prod gate.
/// </summary>
public sealed class MockDropFileWriterTests
{
    private static MockDropFileWriter CreateSut()
    {
        return new MockDropFileWriter(Substitute.For<ILogger<MockDropFileWriter>>());
    }

    [Fact]
    public async Task WriteAsync_ValidPayload_SucceedsAndLeavesFileAwaitingPickup()
    {
        var sut = CreateSut();

        var write = await sut.WriteAsync([0x01, 0x02, 0x03], "[SYD]KIIS90s_TRAFF.wav");

        write.IsSuccess.ShouldBeTrue();
        var probe = await sut.GetPendingWriteTimeAsync("[SYD]KIIS90s_TRAFF.wav");
        probe.IsSuccess.ShouldBeTrue();
        probe.Value.HasValue.ShouldBeTrue();
        sut.Tray["[SYD]KIIS90s_TRAFF.wav"].Bytes.ShouldBe(3);
    }

    [Fact]
    public async Task GetPendingWriteTimeAsync_NeverWritten_ReturnsNoneNotFailure()
    {
        // Absent is the normal collected state on a real share, not an error — the on-air
        // board reads that distinction to decide whether an overwrite is safe.
        var probe = await CreateSut().GetPendingWriteTimeAsync("[CAI]STAR1027_TRAFF.wav");

        probe.IsSuccess.ShouldBeTrue();
        probe.Value.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task WriteAsync_SameNameTwice_KeepsOnlyTheLatestWrite()
    {
        // The playout file name is fixed per market, so a second slot overwrites in place.
        var sut = CreateSut();

        await sut.WriteAsync([0x01], "[SYD]KIIS90s_TRAFF.wav");
        await sut.WriteAsync([0x01, 0x02], "[SYD]KIIS90s_TRAFF.wav");

        sut.Tray.Count.ShouldBe(1);
        sut.Tray["[SYD]KIIS90s_TRAFF.wav"].Bytes.ShouldBe(2);
    }

    [Fact]
    public async Task WriteAsync_EmptyData_ReturnsFailureMatchingTheRealWriters()
    {
        var result = await CreateSut().WriteAsync([], "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Data");
    }

    [Fact]
    public async Task WriteAsync_NullData_ReturnsFailureMatchingTheRealWriters()
    {
        var result = await CreateSut().WriteAsync(null!, "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Data");
    }

    [Fact]
    public async Task WriteAsync_EmptyFileName_ReturnsFailureMatchingTheRealWriters()
    {
        var result = await CreateSut().WriteAsync([0x01], " ");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("File name");
    }

    [Fact]
    public async Task WriteAsync_FailedGuard_LeavesNothingInTheTray()
    {
        var sut = CreateSut();

        await sut.WriteAsync([], "file.wav");

        sut.Tray.ShouldBeEmpty();
    }
}
