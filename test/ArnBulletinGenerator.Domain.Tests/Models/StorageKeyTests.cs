using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public sealed class StorageKeyTests
{
    [Theory]
    [InlineData("adelaide")]
    [InlineData("vp-2fbc21d3f2b9443c88812fd8f091bc47")]
    [InlineData("a50de7bf-0caa-432a-ad9d-fd394750adba")]
    [InlineData("KIIS 101.1")]
    public void IsValid_AcceptsNormalKeys(string key) => StorageKey.IsValid(key).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a#b")]
    [InlineData("a?b")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    public void IsValid_RejectsIllegalOrEmptyKeys(string? key) => StorageKey.IsValid(key).ShouldBeFalse();

    [Fact]
    public void IsValid_RejectsOverlongKey() => StorageKey.IsValid(new string('a', 300)).ShouldBeFalse();
}
