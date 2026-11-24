using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class PronunciationDictionaryServiceTests
{
    private readonly InMemoryRepo _repo = new();
    private readonly FakeProvider _provider = new();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly PronunciationDictionaryService _sut;

    public PronunciationDictionaryServiceTests()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        _sut = new PronunciationDictionaryService(_repo, _provider, _schedules, TimeProvider.System,
            NullLogger<PronunciationDictionaryService>.Instance);
    }

    private static PronunciationRule Alias(string word, string alias) =>
        PronunciationRule.CreateAlias(word, alias).Value;

    [Fact]
    public async Task Create_UploadsRules_StoresReturnedIds()
    {
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], "init"), "operator");

        created.IsSuccess.ShouldBeTrue();
        _provider.CreateCalls.ShouldBe(1);
        created.Value.DictionaryId.ShouldBe("dict-1");
        created.Value.CurrentElevenLabsVersionId.ShouldBe("v1");
        (await _sut.GetAsync(created.Value.Id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_ProviderFails_NothingPersisted()
    {
        _provider.Fail = true;

        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null);

        created.IsFailure.ShouldBeTrue();
        (await _sut.ListAsync()).Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Create_OversizedRules_RejectedBeforeAnyProviderCall()
    {
        // Bug-bash 2026-08-05 finding 6: a rule set whose single version exceeds the Azure Table
        // property ceiling used to upload to ElevenLabs FIRST and fail the local save SECOND,
        // orphaning the upstream dictionary with no local record (observed twice on dev:
        // DictionaryId oaPnWLwGxszGsU5YWubk and 0uR8uZ0Mf4aJnvZ3P2r2). The size check is
        // deterministic and local — it must run before ANY provider call, so the provider is
        // never invoked for a payload that cannot be persisted.
        var rules = Enumerable.Range(0, 400)
            .Select(i => Alias($"W{i:D4}", $"R{i:D4}"))
            .ToList();

        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Huge", null, rules, "init"), "operator");

        created.IsFailure.ShouldBeTrue();
        created.FailureMetadata.HasValue.ShouldBeTrue();
        created.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
        created.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        _provider.CreateCalls.ShouldBe(0);
        _provider.SetCalls.ShouldBe(0);
        _provider.ArchiveCalls.ShouldBe(0);
        (await _sut.ListAsync()).Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Update_OversizedRules_RejectedBeforeAnyProviderCall()
    {
        // Same pre-flight on the edit path: a new version that cannot be persisted must never be
        // pushed upstream (remove/add), which would leave the upstream dictionary ahead of the
        // local record with no reconciliation path.
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null)).Value.Id;
        var rules = Enumerable.Range(0, 400)
            .Select(i => Alias($"W{i:D4}", $"R{i:D4}"))
            .ToList();

        var updated = await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("GC", null, rules, "grow"), "ed");

        updated.IsFailure.ShouldBeTrue();
        updated.FailureMetadata.HasValue.ShouldBeTrue();
        updated.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        _provider.RemoveCalls.ShouldBe(0);
        _provider.AddCalls.ShouldBe(0);
        _provider.SetCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Create_WithPhonemeRules_MintsAliasOnlyVersionLine()
    {
        // Bug-bash 2026-08-05 finding 13: a phoneme-carrying dictionary also mints an alias-only
        // version of the SAME upstream dictionary — the safe attach target for
        // eleven_multilingual_v2, which would delete phoneme-matched words from the audio.
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Melbourne", null,
                [Alias("Warringah", "Warringa"),
                    PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value], "init"), "operator");

        created.IsSuccess.ShouldBeTrue();
        _provider.SetCalls.ShouldBe(1);
        _provider.LastSet.ShouldHaveSingleItem().StringToReplace.ShouldBe("Warringah");
        created.Value.CurrentAliasOnlyLocator.HasValue.ShouldBeTrue();
        created.Value.CurrentAliasOnlyLocator.Value.DictionaryId.ShouldBe(created.Value.DictionaryId);
        created.Value.CurrentAliasOnlyLocator.Value.VersionId.ShouldBe("v2"); // create v1, set-rules v2
    }

    [Fact]
    public async Task Create_AliasOnlyRules_NoAliasOnlyVersionNeeded()
    {
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], "init"), "operator");

        created.IsSuccess.ShouldBeTrue();
        _provider.SetCalls.ShouldBe(0);
        created.Value.CurrentAliasOnlyLocator.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_LocalSaveFails_ArchivesUpstreamAndSurfacesOriginalError()
    {
        // The residual window of finding 6: a late local failure after a successful upstream
        // create must roll the orphan back (archive — ElevenLabs has no hard delete) and ALWAYS
        // surface the ORIGINAL error, never a rollback failure masking it.
        _repo.FailSave = true;

        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], "init"), "operator");

        created.IsFailure.ShouldBeTrue();
        _provider.CreateCalls.ShouldBe(1);
        _provider.ArchiveCalls.ShouldBe(1);
        _provider.LastArchived.ShouldBe("dict-1");
        created.FailureMetadata.HasValue.ShouldBeTrue();
        created.FailureMetadata.Value.Code.ShouldBe("storage.unavailable");
        (await _sut.ListAsync()).Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Create_AliasOnlyMintFails_ArchivesTheFreshDictionary()
    {
        // Set-rules failure after a successful create is the same orphan window: the upstream
        // dictionary exists and nothing references it, so it is rolled back.
        _provider.FailSetRules = true;

        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Melbourne", null,
                [Alias("Warringah", "Warringa"),
                    PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value], "init"), "operator");

        created.IsFailure.ShouldBeTrue();
        _provider.CreateCalls.ShouldBe(1);
        _provider.ArchiveCalls.ShouldBe(1);
        _provider.LastArchived.ShouldBe("dict-1");
    }

    [Fact]
    public async Task Update_AddingPhonemeRules_MintsAliasOnlyVersion()
    {
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Melbourne", null, [Alias("Warringah", "Warringa")], null),
            null)).Value.Id;

        var updated = await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("Melbourne", null,
                [Alias("Warringah", "Warringa"),
                    PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value], "add phoneme"), "ed");

        updated.IsSuccess.ShouldBeTrue();
        _provider.SetCalls.ShouldBe(1);
        updated.Value.CurrentAliasOnlyLocator.HasValue.ShouldBeTrue();
        updated.Value.CurrentElevenLabsVersionId.ShouldBe("v2");
        updated.Value.CurrentAliasOnlyLocator.Value.VersionId.ShouldBe("v3"); // create v1, add v2, set v3
    }

    [Fact]
    public async Task Update_RuleChange_RemovesDropped_AddsRest_StoresFinalAddVersion()
    {
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null)).Value.Id;

        // Drop "Coomera", add "Nerang" → one removal, one add. Final version id comes from the add call.
        var updated = await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("GC", null, [Alias("Nerang", "Nerrang")], "swap"), "ed");

        updated.IsSuccess.ShouldBeTrue();
        _provider.RemoveCalls.ShouldBe(1);
        _provider.LastRemoved.ShouldBe(["Coomera"]);
        _provider.AddCalls.ShouldBe(1);
        updated.Value.CurrentVersion.ShouldBe(2);
        updated.Value.CurrentElevenLabsVersionId.ShouldBe("v3"); // v1 create, v2 remove, v3 add
        updated.Value.CurrentRules.ShouldHaveSingleItem().StringToReplace.ShouldBe("Nerang");
    }

    [Fact]
    public async Task Update_AddOnly_NoRemoveCall()
    {
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null)).Value.Id;

        var updated = await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("GC", null,
                [Alias("Coomera", "Koomera"), Alias("Nerang", "Nerrang")], null), "ed");

        updated.IsSuccess.ShouldBeTrue();
        _provider.RemoveCalls.ShouldBe(0);
        _provider.AddCalls.ShouldBe(1);
        updated.Value.CurrentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Update_NameOnly_NoProviderCalls_NoNewVersion()
    {
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null)).Value.Id;

        var updated = await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("Gold Coast", "desc", [Alias("Coomera", "Koomera")], null), "ed");

        updated.IsSuccess.ShouldBeTrue();
        _provider.AddCalls.ShouldBe(0);
        _provider.RemoveCalls.ShouldBe(0);
        updated.Value.CurrentVersion.ShouldBe(1);
        updated.Value.Name.ShouldBe("Gold Coast");
    }

    [Fact]
    public async Task Restore_ReuploadsPriorRules_AsNewVersion()
    {
        var id = (await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null)).Value.Id;
        await _sut.UpdateAsync(id,
            new UpdatePronunciationDictionaryInput("GC", null, [Alias("Nerang", "Nerrang")], null), null);

        var restored = await _sut.RestoreVersionAsync(id, 1, "ed");

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.CurrentVersion.ShouldBe(3);
        restored.Value.CurrentRules.ShouldHaveSingleItem().StringToReplace.ShouldBe("Coomera");
    }

    [Fact]
    public async Task Update_WithStaleExpectedEtag_ReturnsConflictBeforeProviderWrite()
    {
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null);
        created.Value.ETag = "etag-v2";

        var result = await _sut.UpdateAsync(
            created.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "GC", null, [Alias("Coomera", "Koomerah")], "stale edit", "etag-v1"),
            "ed");

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Code.ShouldBe("pronunciation.revision_mismatch");
        _provider.AddCalls.ShouldBe(0);
        _provider.RemoveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Delete_Existing_RemovesFromGetAndList_WithoutProviderCalls()
    {
        var deletedDictionary = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("GC", null, [Alias("Coomera", "Koomera")], null), null);
        var retainedDictionary = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Melbourne", null, [Alias("Mordialloc", "Mordy-allock")], null), null);
        var createCallsBeforeDelete = _provider.CreateCalls;
        var addCallsBeforeDelete = _provider.AddCalls;
        var removeCallsBeforeDelete = _provider.RemoveCalls;

        var deleted = await _sut.DeleteAsync(deletedDictionary.Value.Id);

        deleted.IsSuccess.ShouldBeTrue();
        (await _sut.GetAsync(deletedDictionary.Value.Id)).IsFailure.ShouldBeTrue();

        var listedIds = (await _sut.ListAsync()).Value.Select(dictionary => dictionary.Id).ToList();
        listedIds.ShouldNotContain(deletedDictionary.Value.Id);
        listedIds.ShouldContain(retainedDictionary.Value.Id);
        _provider.CreateCalls.ShouldBe(createCallsBeforeDelete);
        _provider.AddCalls.ShouldBe(addCallsBeforeDelete);
        _provider.RemoveCalls.ShouldBe(removeCallsBeforeDelete);
    }

    [Fact]
    public async Task Delete_MissingId_FailsWithNotFound()
    {
        var result = await _sut.DeleteAsync("pd-nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
        _provider.CreateCalls.ShouldBe(0);
        _provider.AddCalls.ShouldBe(0);
        _provider.RemoveCalls.ShouldBe(0);
    }

    internal sealed class FakeProvider : IPronunciationDictionaryProvider
    {
        public int CreateCalls;
        public int AddCalls;
        public int RemoveCalls;
        public int SetCalls;
        public int ArchiveCalls;
        public List<string> LastRemoved = [];
        public List<PronunciationRule> LastSet = [];
        public string? LastArchived;
        public bool Fail;
        public bool FailSetRules;
        private int _ver;

        private string NextVer() => $"v{++_ver}";

        public Task<Result<PronunciationLocator>> CreateAsync(
            string name, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(Fail
                ? Result<PronunciationLocator>.Failure("provider down")
                : Result<PronunciationLocator>.Success(new PronunciationLocator("dict-1", NextVer())));
        }

        public Task<Result<string>> AddRulesAsync(
            string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            return Task.FromResult(Fail
                ? Result<string>.Failure("provider down")
                : Result<string>.Success(NextVer()));
        }

        public Task<Result<string>> RemoveRulesAsync(
            string dictionaryId, IReadOnlyList<string> ruleStrings, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            LastRemoved = ruleStrings.ToList();
            return Task.FromResult(Fail
                ? Result<string>.Failure("provider down")
                : Result<string>.Success(NextVer()));
        }

        public Task<Result<string>> SetRulesAsync(
            string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken cancellationToken = default)
        {
            SetCalls++;
            LastSet = rules.ToList();
            return Task.FromResult(Fail || FailSetRules
                ? Result<string>.Failure("provider down")
                : Result<string>.Success(NextVer()));
        }

        public Task<Result> ArchiveAsync(string dictionaryId, CancellationToken cancellationToken = default)
        {
            ArchiveCalls++;
            LastArchived = dictionaryId;
            return Task.FromResult(Fail
                ? Result.Failure("provider down")
                : Result.Success());
        }
    }

    internal sealed class InMemoryRepo : IPronunciationDictionaryRepository
    {
        private readonly Dictionary<string, PronunciationDictionary> _store = new();
        public bool FailSave;

        public Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<PronunciationDictionary>>.Success(_store.Values.ToList()));

        public Task<Result<Option<PronunciationDictionary>>> GetAsync(
            string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Option<PronunciationDictionary>>.Success(
                _store.TryGetValue(id, out var d)
                    ? Option<PronunciationDictionary>.Some(d)
                    : Option<PronunciationDictionary>.None));

        public Task<Result> SaveAsync(PronunciationDictionary dictionary, CancellationToken cancellationToken = default)
        {
            if (FailSave)
                return Task.FromResult(Result.Failure(OperationalFailure.Retryable(
                    "storage.unavailable", "storage unavailable")));
            _store[dictionary.Id] = dictionary;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _store.Remove(id);
            return Task.FromResult(Result.Success());
        }
    }

    // ── Referential integrity on delete ──────────────────────────────

    private static MarketSchedule ScheduleUsingDictionary(string marketId, string station, string dictId) =>
        MarketSchedule.Create(marketId, $"{marketId} Traffic", station, "Australia/Sydney",
            "0 6 * * *", true, CoverageArea.Create(0, 0, 1).Value, "vp-1", "out/", 30, 15, 5,
            DateTime.UtcNow, DateTime.UtcNow, "", dictId).Value;

    [Fact]
    public async Task Delete_WhenAStationStillReferencesIt_IsRefusedAndNamesTheStation()
    {
        // Assignment already validates in one direction (you cannot point a schedule at a missing
        // id), but deletion did not, so an in-use dictionary could be removed out from under a
        // station. Proven on dev 2026-07-28: melbourne's dictionary deleted with a silent 204,
        // leaving a dangling reference that makes bulletins air with no pronunciation corrections.
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Melbourne Roads", null, [Alias("Keilor", "Keeler")], "init"),
            "operator");
        created.IsSuccess.ShouldBeTrue();
        string id = created.Value.Id;

        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [ScheduleUsingDictionary("melbourne", "KIIS 101.1", id)]));

        var deleted = await _sut.DeleteAsync(id);

        deleted.IsFailure.ShouldBeTrue();
        deleted.Error.ShouldContain("KIIS 101.1");
        deleted.FailureMetadata.HasValue.ShouldBeTrue();
        deleted.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Conflict);
        (await _sut.GetAsync(id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_WhenNoStationReferencesIt_StillSucceeds()
    {
        // Guard against over-blocking: an unassigned dictionary must remain deletable.
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Spare", null, [Alias("Keilor", "Keeler")], "init"), "operator");
        string id = created.Value.Id;

        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [ScheduleUsingDictionary("sydney", "KIIS 106.5", "pd-someone-else")]));

        (await _sut.DeleteAsync(id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_WhenTheScheduleListCannotBeRead_FailsClosed()
    {
        // This gates an irreversible delete, so "could not verify" must block, not wave through.
        var created = await _sut.CreateAsync(
            new CreatePronunciationDictionaryInput("Spare", null, [Alias("Keilor", "Keeler")], "init"), "operator");
        string id = created.Value.Id;

        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("storage unavailable"));

        var deleted = await _sut.DeleteAsync(id);

        deleted.IsFailure.ShouldBeTrue();
        (await _sut.GetAsync(id)).IsSuccess.ShouldBeTrue();
    }
}

// ── Platform-wide (global) scope ──────────────────────────────────────

public sealed class PronunciationDictionaryScopeTests
{
    private readonly PronunciationDictionaryServiceTests.InMemoryRepo _repo = new();
    private readonly PronunciationDictionaryServiceTests.FakeProvider _provider = new();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly PronunciationDictionaryService _sut;

    public PronunciationDictionaryScopeTests()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        _sut = new PronunciationDictionaryService(_repo, _provider, _schedules, TimeProvider.System,
            NullLogger<PronunciationDictionaryService>.Instance);
    }

    private static PronunciationRule Alias(string word, string alias) =>
        PronunciationRule.CreateAlias(word, alias).Value;

    private Task<Result<PronunciationDictionary>> Create(
        string name, PronunciationScope scope = PronunciationScope.Station) =>
        _sut.CreateAsync(
            new CreatePronunciationDictionaryInput(name, null, [Alias("Keilor", "Keeler")], "init", Scope: scope),
            "operator");

    [Fact]
    public async Task Create_DefaultsToStationScope()
    {
        var created = await Create("Melbourne Roads");

        created.Value.Scope.ShouldBe(PronunciationScope.Station);
        created.Value.IsGlobal.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_Global_IsPersistedAsGlobal()
    {
        var created = await Create("Everywhere", PronunciationScope.Global);

        created.IsSuccess.ShouldBeTrue();
        created.Value.IsGlobal.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_SecondGlobal_IsRefusedAsAConflictNamingTheIncumbent()
    {
        // Exactly one keeps the emitted locator array at a fixed two, inside the provider's cap of
        // three, and stops "which platform-wide dictionary won?" from ever being askable.
        await Create("Everywhere", PronunciationScope.Global);

        var second = await Create("Also Everywhere", PronunciationScope.Global);

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldContain("Everywhere");
        second.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Conflict);
    }

    [Fact]
    public async Task Update_PromotingToGlobal_PersistsWithoutMintingAVersion()
    {
        // Reach is not rule content: a version row per promotion would pollute the audit trail
        // every station reads.
        var created = await Create("Melbourne Roads");
        int versionsBefore = created.Value.Versions.Count;

        var promoted = await _sut.UpdateAsync(created.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Melbourne Roads", null, created.Value.CurrentRules, "promote",
                Scope: PronunciationScope.Global),
            "operator");

        promoted.IsSuccess.ShouldBeTrue();
        promoted.Value.IsGlobal.ShouldBeTrue();
        promoted.Value.Versions.Count.ShouldBe(versionsBefore);
        (await _sut.GetAsync(created.Value.Id)).Value.IsGlobal.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_PromotingASecondDictionary_IsRefused()
    {
        await Create("Everywhere", PronunciationScope.Global);
        var station = await Create("Melbourne Roads");

        var promoted = await _sut.UpdateAsync(station.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Melbourne Roads", null, station.Value.CurrentRules, "promote",
                Scope: PronunciationScope.Global),
            "operator");

        promoted.IsFailure.ShouldBeTrue();
        (await _sut.GetAsync(station.Value.Id)).Value.IsGlobal.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_ReSavingTheGlobalItself_IsNotAConflictWithItself()
    {
        var global = await Create("Everywhere", PronunciationScope.Global);

        var edited = await _sut.UpdateAsync(global.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Everywhere", "now described", global.Value.CurrentRules, "describe",
                Scope: PronunciationScope.Global),
            "operator");

        edited.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_ActiveGlobal_IsRefused()
    {
        // The assignment guard cannot see it: no schedule references a platform-wide dictionary, so
        // without an explicit check an admin could delete what every station is reading from.
        var global = await Create("Everywhere", PronunciationScope.Global);

        var deleted = await _sut.DeleteAsync(global.Value.Id);

        deleted.IsFailure.ShouldBeTrue();
        deleted.Error.ShouldContain("platform-wide");
        (await _sut.GetAsync(global.Value.Id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Archive_ActiveGlobal_IsRefused()
    {
        var global = await Create("Everywhere", PronunciationScope.Global);

        var archived = await _sut.ArchiveAsync(global.Value.Id);

        archived.IsFailure.ShouldBeTrue();
        (await _sut.GetAsync(global.Value.Id)).Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_AfterDemotion_Succeeds()
    {
        var global = await Create("Everywhere", PronunciationScope.Global);

        var demoted = await _sut.UpdateAsync(global.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Everywhere", null, global.Value.CurrentRules, "demote",
                Scope: PronunciationScope.Station),
            "operator");
        demoted.IsSuccess.ShouldBeTrue();

        (await _sut.DeleteAsync(global.Value.Id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Unarchive_WhenAnotherGlobalTookOver_IsRefused()
    {
        // Two active globals must not be reachable by the back door either: demote, archive,
        // promote a replacement, then restore the original.
        var original = await Create("Everywhere", PronunciationScope.Global);
        await _sut.UpdateAsync(original.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Everywhere", null, original.Value.CurrentRules, "demote", Scope: PronunciationScope.Station),
            "operator");
        (await _sut.ArchiveAsync(original.Value.Id)).IsSuccess.ShouldBeTrue();

        // Re-promote the archived one so restoring it would clash, then take the slot.
        var archived = await _sut.GetAsync(original.Value.Id);
        await _sut.UpdateAsync(archived.Value.Id,
            new UpdatePronunciationDictionaryInput(
                "Everywhere", null, archived.Value.CurrentRules, "re-promote", Scope: PronunciationScope.Global),
            "operator");
        await Create("Replacement", PronunciationScope.Global);

        var restored = await _sut.UnarchiveAsync(original.Value.Id);

        restored.IsFailure.ShouldBeTrue();
        restored.Error.ShouldContain("Replacement");
    }
}
