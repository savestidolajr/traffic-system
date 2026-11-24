using System.Globalization;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public sealed class JobProgressMappingTests
{
    [Fact]
    public void ToProgressDto_MapsEnumNamesTimestampsAndEventPayload()
    {
        var createdAt = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.FromHours(10));
        var stageStartedAt = createdAt.AddSeconds(1);
        var callAt = createdAt.AddSeconds(2);
        var counts = new Dictionary<string, double>
        {
            ["words"] = 84,
            ["predictedSeconds"] = 28.3
        };
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, createdAt);
        document.Append(stageStartedAt, 2, ProgressStage.Compose, ProgressEventKind.StageStarted,
            "Compose started.");
        document.Append(callAt, 2, ProgressStage.Compose, ProgressEventKind.LlmCall,
            "composer completed in 1234ms.", counts, durationMs: 1_234, model: "model-a", route: "compose");

        var dto = document.ToProgressDto();

        dto.Status.ShouldBe(nameof(JobStatus.Requested));
        dto.Stage.ShouldBe(nameof(ProgressStage.Compose));
        dto.StageStartedAt.ShouldBe(Iso(stageStartedAt));
        dto.UpdatedAt.ShouldBe(Iso(callAt));
        dto.Events.Count.ShouldBe(2);
        var call = dto.Events[1];
        call.At.ShouldBe(Iso(callAt));
        call.Attempt.ShouldBe(2);
        call.Stage.ShouldBe(nameof(ProgressStage.Compose));
        call.Kind.ShouldBe(nameof(ProgressEventKind.LlmCall));
        call.Counts.ShouldNotBeNull();
        call.Counts!["words"].ShouldBe(84);
        call.Counts["predictedSeconds"].ShouldBe(28.3);
        call.DurationMs.ShouldBe(1_234);
        call.Model.ShouldBe("model-a");
        call.Route.ShouldBe("compose");
    }

    [Fact]
    public void ToProgressDto_WhenTypicalDurationsAreAbsent_LeavesTypicalSecondsNull()
    {
        var document = new JobProgressDocument(
            "job-1", "sydney", JobStatus.Requested,
            new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero));

        var dto = document.ToProgressDto();

        dto.TypicalSeconds.ShouldBeNull();
    }

    [Fact]
    public void ToProgressDto_WithTypicalDurations_PassesThemThrough()
    {
        var document = new JobProgressDocument(
            "job-1", "sydney", JobStatus.Requested,
            new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero));
        var typical = new Dictionary<string, double> { [nameof(ProgressStage.Write)] = 4.5 };

        var dto = document.ToProgressDto(typical);

        dto.TypicalSeconds.ShouldNotBeNull();
        dto.TypicalSeconds![nameof(ProgressStage.Write)].ShouldBe(4.5);
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
