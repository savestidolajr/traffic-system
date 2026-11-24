using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     The review cockpit's pronunciation fix, addressed by job. The behaviours worth defending are
///     the two that cost real on-air corrections: the word's rule is REPLACED rather than a second
///     one appended (the provider applies one per word, so an append is a silently dead fix), and a
///     concurrent dictionary edit is replayed rather than clobbered.
/// </summary>
public sealed class JobPronunciationServiceTests
{
    private const string AiredDictionaryId = "el-dict-1";
    private const string JobId = "job-1";

    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();

    private readonly IPronunciationDictionaryService _dictionaries =
        Substitute.For<IPronunciationDictionaryService>();

    private readonly List<UpdatePronunciationDictionaryInput> _writes = [];
    private readonly JobPronunciationService _sut;

    public JobPronunciationServiceTests()
    {
        _jobs.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(JobAiredWith(AiredDictionaryId)));

        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([Library(Coomera())]));

        _dictionaries.UpdateAsync(Arg.Any<string>(), Arg.Any<UpdatePronunciationDictionaryInput>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.ArgAt<UpdatePronunciationDictionaryInput>(1);
                _writes.Add(input);
                return Result<PronunciationDictionary>.Success(Library([.. input.Rules]));
            });

        _sut = new JobPronunciationService(_jobs, _dictionaries,
            Substitute.For<ILogger<JobPronunciationService>>());
    }

    [Fact]
    public async Task AddAliasRule_WordWithNoRule_AppendsOne()
    {
        var result = await _sut.AddAliasRuleAsync(JobId, "Nerang", "Nerrang", "Tim");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(AliasEditOutcome.Added);
        _writes.ShouldHaveSingleItem();
        _writes[0].Rules.Select(r => r.StringToReplace).ShouldBe(["Coomera", "Nerang"]);
        _writes[0].ChangeNote.ShouldBe("Added \"Nerang\" from bulletin review");
    }

    [Fact]
    public async Task AddAliasRule_WordAlreadyRuled_ReplacesItRatherThanAppending()
    {
        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomerah", "Tim");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(AliasEditOutcome.Changed);
        // One rule, not two: a second rule for the same word leaves the original in force and the
        // correction never reaches air.
        _writes[0].Rules.ShouldHaveSingleItem();
        _writes[0].Rules[0].Replacement.ShouldBe("Koomerah");
        _writes[0].ChangeNote.ShouldBe("Changed \"Coomera\" from bulletin review");
    }

    [Fact]
    public async Task AddAliasRule_MatchesTheWordCaseInsensitively()
    {
        await _sut.AddAliasRuleAsync(JobId, "coomera", "Koomerah", "Tim");

        _writes[0].Rules.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AddAliasRule_AlreadySaysExactlyThis_WritesNothing()
    {
        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomera", "Tim");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(AliasEditOutcome.AlreadyCorrect);
        _writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddAliasRule_BulletinAiredWithNoDictionary_Fails()
    {
        _jobs.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create(JobId, "SYD", "sydney", 30.0, DateTime.UtcNow).Value));

        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomerah", "Tim");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobPronunciationService.NoDictionaryOnJob);
        _writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddAliasRule_AiredDictionaryNoLongerInLibrary_Fails()
    {
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));

        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomerah", "Tim");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobPronunciationService.DictionaryNotInLibrary);
    }

    [Fact]
    public async Task AddAliasRule_ConcurrentEdit_ReplaysOnceAndKeepsTheOtherEdit()
    {
        // Someone else added Nerang between our read and our write.
        var fresh = Library([Coomera(), Alias("Nerang", "Nerrang")]);
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<PronunciationDictionary>>.Success([Library(Coomera())]),
                Result<IReadOnlyList<PronunciationDictionary>>.Success([fresh]));

        bool firstWrite = true;
        _dictionaries.UpdateAsync(Arg.Any<string>(), Arg.Any<UpdatePronunciationDictionaryInput>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.ArgAt<UpdatePronunciationDictionaryInput>(1);
                _writes.Add(input);
                if (!firstWrite)
                    return Result<PronunciationDictionary>.Success(Library([.. input.Rules]));
                firstWrite = false;
                return Result<PronunciationDictionary>.Failure(OperationalFailure.Conflict(
                    "pronunciation.revision_mismatch", "Conflict: the dictionary changed."));
            });

        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomerah", "Tim");

        result.IsSuccess.ShouldBeTrue();
        _writes.Count.ShouldBe(2);
        // The replay carries the other operator's rule as well as the fix — a lost update here
        // would silently delete someone else's correction.
        _writes[1].Rules.Select(r => r.StringToReplace).ShouldBe(["Coomera", "Nerang"]);
        _writes[1].Rules.Single(r => r.StringToReplace == "Coomera").Replacement.ShouldBe("Koomerah");
    }

    [Fact]
    public async Task AddAliasRule_SustainedConflict_StopsAfterOneReplay()
    {
        _dictionaries.UpdateAsync(Arg.Any<string>(), Arg.Any<UpdatePronunciationDictionaryInput>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<PronunciationDictionary>.Failure(OperationalFailure.Conflict(
                "pronunciation.revision_mismatch", "Conflict: the dictionary changed.")));

        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "Koomerah", "Tim");

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task AddAliasRule_BlankReplacement_FailsWithoutWriting()
    {
        var result = await _sut.AddAliasRuleAsync(JobId, "Coomera", "   ", "Tim");

        result.IsFailure.ShouldBeTrue();
        _writes.ShouldBeEmpty();
    }

    private static Job JobAiredWith(string elevenLabsDictionaryId)
    {
        var job = Job.Create(JobId, "SYD", "sydney", 30.0, DateTime.UtcNow).Value;
        job.PronunciationDictionaryId = Option<string>.Some(elevenLabsDictionaryId);
        return job;
    }

    private static PronunciationRule Coomera() => Alias("Coomera", "Koomera");

    private static PronunciationRule Alias(string word, string replacement) =>
        PronunciationRule.CreateAlias(word, replacement).Value;

    private static PronunciationDictionary Library(PronunciationRule rule) => Library([rule]);

    private static PronunciationDictionary Library(IReadOnlyList<PronunciationRule> rules)
    {
        var dictionary = PronunciationDictionary.Create(
            "Sydney Roads", null, AiredDictionaryId, "el-ver-1", rules, "seed", "test",
            DateTime.UtcNow, "pd-sydney").Value;
        dictionary.ETag = "etag-1";
        return dictionary;
    }
}
