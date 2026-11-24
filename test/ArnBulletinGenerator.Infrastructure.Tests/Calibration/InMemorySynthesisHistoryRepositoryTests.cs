using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Calibration;

public sealed class InMemorySynthesisHistoryRepositoryTests
{
    private static InMemorySynthesisHistoryRepository CreateSut()
    {
        return new InMemorySynthesisHistoryRepository(persistencePath: null, Substitute.For<ILogger<InMemorySynthesisHistoryRepository>>());
    }

    private static SynthesisRecord MakeRecord(
        string voiceId = "voice-1",
        string modelId = "eleven_v3",
        double speed = 1.0,
        int syllables = 150,
        double duration = 30.0,
        int seed = 42)
    {
        return new SynthesisRecord
        {
            VoiceId = voiceId,
            ModelId = modelId,
            Speed = speed,
            SyllableCount = syllables,
            WordCount = (int)(syllables / 1.5),
            AchievedDuration = duration,
            TargetDuration = 30.0,
            Seed = seed,
            ScriptPreview = "Test script",
            Timestamp = DateTimeOffset.UtcNow,
            WasWithinTolerance = true
        };
    }

    [Fact]
    public void Constructor_LoadsHistory_WrittenWithPascalCaseProperties()
    {
        // The committed replay corpus (test/corpus/fallback-replay/synthesis-history.json) and any
        // export taken with default serializer options are PascalCase. This loader serialises
        // camelCase, so without case-insensitive reads it deserialised NOTHING and swallowed the
        // failure — the predictor then ran on static-rate constants with no signal that its history
        // was empty. Round-tripping its own output always worked, which is why it went unnoticed.
        string path = Path.Combine(Path.GetTempPath(), $"arn-hist-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """
            [
              {
                "VoiceId": "voice-1",
                "ModelId": "eleven_v3",
                "Seed": null,
                "Speed": 1.0,
                "SyllableCount": 120,
                "WordCount": 80,
                "AchievedDuration": 24.5,
                "TargetDuration": 22.0,
                "ScriptPreview": "Pascal-cased export",
                "Timestamp": "2026-08-18T00:00:00+00:00",
                "WasWithinTolerance": false
              }
            ]
            """);
        try
        {
            var repo = new InMemorySynthesisHistoryRepository(
                path, Substitute.For<ILogger<InMemorySynthesisHistoryRepository>>());

            repo.Count.ShouldBe(1);
            repo.GetHistory("voice-1", "eleven_v3").Single().SyllableCount.ShouldBe(120);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_AddsEntry_ToHistory()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord());

        repo.Count.ShouldBe(1);
    }

    [Fact]
    public void FindClosestBySyllables_MatchingVoice_ReturnsResults()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord(voiceId: "voice-1", syllables: 150));
        repo.Record(MakeRecord(voiceId: "voice-1", syllables: 160));
        repo.Record(MakeRecord(voiceId: "voice-2", syllables: 150)); // different voice

        var results = repo.FindClosestBySyllables("voice-1", "eleven_v3", 1.0, 155);

        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.VoiceId == "voice-1");
    }

    [Fact]
    public void FindClosestBySyllables_DifferentModel_ReturnsEmpty()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord(modelId: "eleven_v3")); // only v3 records

        var results = repo.FindClosestBySyllables("voice-1", "eleven_turbo_v2_5", 1.0, 150);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void FindClosestBySyllables_ReturnsNearest_BySyllableCount()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord(syllables: 100, duration: 20.0));
        repo.Record(MakeRecord(syllables: 200, duration: 40.0));
        repo.Record(MakeRecord(syllables: 150, duration: 30.0)); // exact match

        var results = repo.FindClosestBySyllables("voice-1", "eleven_v3", 1.0, 155, 3);

        results[0].SyllableCount.ShouldBe(150); // closest
        results[1].SyllableCount.ShouldBe(200);
        results[2].SyllableCount.ShouldBe(100);
    }

    [Fact]
    public void FindClosestBySyllables_SpeedMismatch_ReturnsEmpty()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord(speed: 1.0));

        var results = repo.FindClosestBySyllables("voice-1", "eleven_v3", 0.85, 150);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void FindClosestByDuration_ReturnsNearest()
    {
        var repo = CreateSut();
        repo.Record(MakeRecord(duration: 28.0));
        repo.Record(MakeRecord(duration: 32.0));
        repo.Record(MakeRecord(duration: 30.0)); // exact match

        var results = repo.FindClosestByDuration("voice-1", "eleven_v3", 1.0, 30.5, 3);

        results[0].AchievedDuration.ShouldBe(30.0); // closest to 30.5
    }

    [Fact]
    public void Count_ReturnsTotalRecords()
    {
        var repo = CreateSut();
        repo.Count.ShouldBe(0);

        repo.Record(MakeRecord());
        repo.Record(MakeRecord());

        repo.Count.ShouldBe(2);
    }

    [Fact]
    public void GetHistory_ReturnsOrderedByTimestamp_Descending()
    {
        var repo = CreateSut();
        var old = MakeRecord(seed: 1);
        var newer = MakeRecord(seed: 2);

        repo.Record(old);
        Thread.Sleep(1);
        repo.Record(newer);

        var history = repo.GetHistory("voice-1", "eleven_v3");

        history[0].Seed.ShouldBe(2); // newest first
        history[1].Seed.ShouldBe(1);
    }
}
