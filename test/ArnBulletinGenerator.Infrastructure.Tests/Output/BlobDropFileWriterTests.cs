using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Output;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class BlobDropFileWriterTests
{
    private static BlobDropFileWriter CreateSut(ZettaOptions options)
    {
        return new BlobDropFileWriter(
            Options.Create(options),
            Substitute.For<TokenCredential>(),
            Substitute.For<ILogger<BlobDropFileWriter>>());
    }

    private static ZettaOptions ConfiguredOptions()
    {
        return new ZettaOptions
        {
            Enabled = true,
            Transport = DeliveryTransport.BlobDrop,
            DropAccountName = "ibpauetststaftp01",
            DropContainerName = "informationbulletins"
        };
    }

    [Fact]
    public async Task WriteAsync_NullData_ReturnsFailure()
    {
        var result = await CreateSut(ConfiguredOptions()).WriteAsync(null!, "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Data");
    }

    [Fact]
    public async Task WriteAsync_EmptyData_ReturnsFailure()
    {
        var result = await CreateSut(ConfiguredOptions()).WriteAsync([], "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Data");
    }

    [Fact]
    public async Task WriteAsync_EmptyFileName_ReturnsFailure()
    {
        var result = await CreateSut(ConfiguredOptions()).WriteAsync([0x01], " ");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("File name");
    }

    [Fact]
    public async Task WriteAsync_UnconfiguredDropAccount_ReturnsFailure()
    {
        var options = ConfiguredOptions();
        options.DropAccountName = "";

        var result = await CreateSut(options).WriteAsync([0x01], "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("drop account");
    }

    [Fact]
    public async Task WriteAsync_UnconfiguredDropContainer_ReturnsFailure()
    {
        var options = ConfiguredOptions();
        options.DropContainerName = "";

        var result = await CreateSut(options).WriteAsync([0x01], "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("drop container");
    }
}
