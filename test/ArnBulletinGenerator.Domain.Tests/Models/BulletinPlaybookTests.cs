using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class BulletinPlaybookTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("writer", "writer")]
    [InlineData("compose2", "compose2")]
    [InlineData("writeIncidents", "writeIncidents")]
    [InlineData("writeFlow", "writeFlow")]
    [InlineData("edit", "edit")]
    public void CanonicalRole_ReturnsSupportedRoleWithCanonicalCasing(string input, string expected)
    {
        BulletinPlaybook.CanonicalRole(input).ShouldBe(expected);
    }

    [Fact]
    public void CanonicalRole_MixedCaseInput_ReturnsCanonicalCasing()
    {
        BulletinPlaybook.CanonicalRole("WRITEFLOW").ShouldBe("writeFlow");
    }

    [Fact]
    public void CanonicalRole_UnsupportedRole_ReturnsNull()
    {
        BulletinPlaybook.CanonicalRole("unknown").ShouldBeNull();
    }

    [Fact]
    public void Create_WriteFlow_SucceedsAndPreservesCanonicalRole()
    {
        var result = BulletinPlaybook.Create(
            "Flow prompts", null, "writeFlow", PlaybookOverlay.None, null, "tester", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Role.ShouldBe("writeFlow");
    }
}
