using ArnBulletinGenerator.Infrastructure.Output;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public class ConsoleBroadcastOutputTests
{
    private readonly ConsoleBroadcastOutput _sut = new(Substitute.For<ILogger<ConsoleBroadcastOutput>>());

    [Fact]
    public async Task SaveAsync_ShouldReturnSuccess()
    {
        (await _sut.SaveAsync([1, 2, 3], "test_id")).IsSuccess.ShouldBeTrue();
    }
}
