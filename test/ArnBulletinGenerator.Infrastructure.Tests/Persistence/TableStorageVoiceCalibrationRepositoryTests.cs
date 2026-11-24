using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageVoiceCalibrationRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageVoiceCalibrationRepositoryTests
{
    private readonly ILogger<TableStorageVoiceCalibrationRepository> _logger =
        Substitute.For<ILogger<TableStorageVoiceCalibrationRepository>>();

    private readonly TableStorageVoiceCalibrationRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageVoiceCalibrationRepositoryTests()
    {
        _sut = new TableStorageVoiceCalibrationRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static VoiceSettings DefaultSettings => VoiceSettings.Default;

    private static CalibrationEntity CreateEntity(string voiceId = "voice-1")
    {
        return new CalibrationEntity
        {
            PartitionKey = "Calibration",
            RowKey = DefaultSettings.GetConfigurationKey(voiceId),
            VoiceId = voiceId,
            Provider = "ElevenLabs",
            SyllablesPerSecond = 5.2,
            SyllablesPerWord = 1.45,
            WordsPerMinute = 215,
            SyllablesPerMinute = 312,
            RunCount = 3,
            ReferenceSyllableCount = 100,
            ReferenceDurationSeconds = 19.2,
            CalibratedAt = DateTime.UtcNow.AddDays(-5),
            ValidForDays = 30,
            Stability = DefaultSettings.Stability,
            SimilarityBoost = DefaultSettings.SimilarityBoost,
            Style = DefaultSettings.Style,
            UseSpeakerBoost = DefaultSettings.UseSpeakerBoost,
            Speed = DefaultSettings.Speed
        };
    }

    // ── GetCalibrationAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetCalibrationAsync_Existing_ReturnsSome()
    {
        var entity = CreateEntity();
        _tableClient.GetEntityAsync<CalibrationEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        var result = await _sut.GetCalibrationAsync("voice-1", DefaultSettings, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        var cal = result.Value.Value;
        cal.VoiceId.ShouldBe("voice-1");
        cal.SyllablesPerSecond.ShouldBe(5.2);
        cal.RunCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetCalibrationAsync_NonExistent_ReturnsNone()
    {
        _tableClient.GetEntityAsync<CalibrationEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.GetCalibrationAsync("unknown", DefaultSettings, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCalibrationAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<CalibrationEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Server Error"));

        var result = await _sut.GetCalibrationAsync("voice-1", DefaultSettings, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    // ── SaveCalibrationAsync ─────────────────────────────────────────

    [Fact]
    public async Task SaveCalibrationAsync_Success_UpsertsCalled()
    {
        var cal = new VoiceCalibration("key-1", "voice-1", DefaultSettings)
        {
            Provider = "ElevenLabs",
            SyllablesPerSecond = 5.0,
            SyllablesPerWord = 1.45,
            WordsPerMinute = 207,
            SyllablesPerMinute = 300,
            RunCount = 1,
            ReferenceSyllableCount = 50,
            ReferenceDurationSeconds = 10
        };

        var result = await _sut.SaveCalibrationAsync(cal, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<CalibrationEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCalibrationAsync_StorageError_ReturnsFailure()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<CalibrationEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var cal = new VoiceCalibration("key-err", "v-err", DefaultSettings);

        var result = await _sut.SaveCalibrationAsync(cal, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    // ── Mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_RoundTrips_AllFields()
    {
        var cal = new VoiceCalibration("key-rt", "voice-rt", DefaultSettings)
        {
            Provider = "ElevenLabs",
            SyllablesPerSecond = 4.8,
            SyllablesPerWord = 1.5,
            WordsPerMinute = 192,
            SyllablesPerMinute = 288,
            RunCount = 5,
            ReferenceSyllableCount = 80,
            ReferenceDurationSeconds = 16.7,
            ValidFor = TimeSpan.FromDays(14)
        };

        CalibrationEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<CalibrationEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveCalibrationAsync(cal, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.VoiceId.ShouldBe("voice-rt");
        captured.RunCount.ShouldBe(5);
        captured.ValidForDays.ShouldBe(14);

        // Feed entity back through Get to test domain mapping
        _tableClient.GetEntityAsync<CalibrationEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(captured));

        var loaded = (await _sut.GetCalibrationAsync("voice-rt", DefaultSettings, CancellationToken.None)).Value.Value;
        loaded.SyllablesPerSecond.ShouldBe(4.8);
        loaded.RunCount.ShouldBe(5);
        loaded.ValidFor.ShouldBe(TimeSpan.FromDays(14));
    }

    [Fact]
    public async Task MapToDomain_DefaultsSyllablesPerWordWhenZero()
    {
        var entity = CreateEntity();
        entity.SyllablesPerWord = 0; // should fall back to CalibrationDefaults

        _tableClient.GetEntityAsync<CalibrationEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        var result = await _sut.GetCalibrationAsync("voice-1", DefaultSettings, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.SyllablesPerWord.ShouldBe(CalibrationDefaults.SyllablesPerWord);
    }
}
