using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Conversational;

public sealed class ShippedPlaybookProviderTests
{
    [Theory]
    [InlineData("compose2")]
    [InlineData("COMPOSE2")]
    [InlineData("writeIncidents")]
    [InlineData("writeincidents")]
    [InlineData("WRITEINCIDENTS")]
    [InlineData("writeFlow")]
    [InlineData("writeflow")]
    [InlineData("WRITEFLOW")]
    [InlineData("writer")]
    [InlineData("WRITER")]
    public void Resolves_and_parses_supported_roles_regardless_of_casing(string role)
    {
        var provider = new ShippedPlaybookProvider();

        var result = provider.GetDefault(role);

        result.IsSuccess.ShouldBeTrue($"Role '{role}' failed: {result.Error}");
        result.Value.Blocks.Count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_when_role_is_empty(string role)
    {
        var provider = new ShippedPlaybookProvider();

        var result = provider.GetDefault(role);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("required");
    }

    [Fact]
    public void Fails_when_role_is_unknown()
    {
        var provider = new ShippedPlaybookProvider();

        var result = provider.GetDefault("unknown-role");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Unknown playbook role");
    }
}
