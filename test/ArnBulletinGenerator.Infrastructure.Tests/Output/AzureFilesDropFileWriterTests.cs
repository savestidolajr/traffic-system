using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Output;
using Azure;
using Azure.Core;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class AzureFilesDropFileWriterTests
{
    private static AzureFilesDropFileWriter CreateSut(ZettaOptions options)
    {
        return new AzureFilesDropFileWriter(
            Options.Create(options),
            Substitute.For<TokenCredential>(),
            Substitute.For<ILogger<AzureFilesDropFileWriter>>());
    }

    private static ZettaOptions ConfiguredOptions()
    {
        return new ZettaOptions
        {
            Enabled = true,
            Transport = DeliveryTransport.AzureFilesDrop,
            DropAccountName = "ibpauetststaftp01",
            DropFileShareName = "informationbulletins"
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
    public async Task WriteAsync_UnconfiguredDropFileShare_ReturnsFailure()
    {
        var options = ConfiguredOptions();
        options.DropFileShareName = "";

        var result = await CreateSut(options).WriteAsync([0x01], "file.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("drop file share");
    }

    // ── SMB SharingViolation retry ─────────────────────────────────────

    private readonly ShareDirectoryClient _directoryClient = Substitute.For<ShareDirectoryClient>();
    private readonly ShareFileClient _fileClient = Substitute.For<ShareFileClient>();
    private readonly ILogger<AzureFilesDropFileWriter> _logger = Substitute.For<ILogger<AzureFilesDropFileWriter>>();

    private AzureFilesDropFileWriter CreateSut(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _directoryClient.GetFileClient(Arg.Any<string>()).Returns(_fileClient);
        return new AzureFilesDropFileWriter(
            Options.Create(ConfiguredOptions()),
            Substitute.For<TokenCredential>(),
            _logger,
            _directoryClient,
            delay);
    }

    private static RequestFailedException SharingViolation() =>
        new(409, "The specified resource may be in use by an SMB client.", "SharingViolation", null);

    private int LogCount(LogLevel level) =>
        _logger.ReceivedCalls().Count(c => (LogLevel?)c.GetArguments()[0] == level);

    private int CreateCallCount() =>
        _fileClient.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ShareFileClient.CreateAsync));

    [Fact]
    public async Task WriteAsync_SharingViolationOnFirstAttempt_RetriesAndSucceeds()
    {
        var createCalls = 0;
        _fileClient.CreateAsync(
                Arg.Any<long>(),
                Arg.Any<ShareFileCreateOptions>(),
                Arg.Any<ShareFileRequestConditions>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                createCalls++;
                if (createCalls == 1)
                    throw SharingViolation();
                return Substitute.For<Response<ShareFileInfo>>();
            });
        _fileClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<IProgress<long>>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response<ShareFileUploadInfo>>());

        var delays = new List<TimeSpan>();
        var sut = CreateSut(delay: (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var result = await sut.WriteAsync([0x01, 0x02], "[SYD]KIIS90s_TRAFF.wav");

        result.IsSuccess.ShouldBeTrue();
        createCalls.ShouldBe(2);
        delays.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMilliseconds(300));
        LogCount(LogLevel.Warning).ShouldBe(1);
        LogCount(LogLevel.Error).ShouldBe(0);
    }

    [Fact]
    public async Task WriteAsync_SharingViolationNeverClears_ReturnsFailureWithExistingMessage()
    {
        _fileClient.CreateAsync(
                Arg.Any<long>(),
                Arg.Any<ShareFileCreateOptions>(),
                Arg.Any<ShareFileRequestConditions>(),
                Arg.Any<CancellationToken>())
            .Throws(SharingViolation());
        _fileClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<IProgress<long>>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response<ShareFileUploadInfo>>());

        var sut = CreateSut(delay: (_, _) => Task.CompletedTask);

        var result = await sut.WriteAsync([0x01, 0x02], "[SYD]KIIS90s_TRAFF.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldStartWith("Azure Files drop write failed:");
        result.Error.ShouldContain("in use by an SMB client");
        CreateCallCount().ShouldBe(3);
        LogCount(LogLevel.Warning).ShouldBe(2);
        LogCount(LogLevel.Error).ShouldBe(1);
    }

    [Fact]
    public async Task WriteAsync_NonTransientRequestFailedException_IsNotRetried()
    {
        _fileClient.CreateAsync(
                Arg.Any<long>(),
                Arg.Any<ShareFileCreateOptions>(),
                Arg.Any<ShareFileRequestConditions>(),
                Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(
                403, "The account being accessed does not support http.", "AuthorizationPermissionMismatch", null));
        _fileClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<IProgress<long>>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response<ShareFileUploadInfo>>());

        var delayCalled = false;
        var sut = CreateSut(delay: (_, _) =>
        {
            delayCalled = true;
            return Task.CompletedTask;
        });

        var result = await sut.WriteAsync([0x01, 0x02], "[SYD]KIIS90s_TRAFF.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Azure Files drop write failed");
        CreateCallCount().ShouldBe(1);
        delayCalled.ShouldBeFalse();
        LogCount(LogLevel.Warning).ShouldBe(0);
        LogCount(LogLevel.Error).ShouldBe(1);
    }
}
