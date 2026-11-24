using System.Net;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the request-body guards added to <see cref="PronunciationDictionariesHttpTrigger" />'s
///     Create/Update: Name/Description length caps (M2), a minimum-one-rule requirement on Create
///     (L7), and a case-insensitive duplicate-name guard on Create (L6).
/// </summary>
public sealed class PronunciationDictionariesHttpTriggerTests
{
    private readonly IPronunciationDictionaryService _dictionaries = Substitute.For<IPronunciationDictionaryService>();
    private readonly PronunciationDictionariesHttpTrigger _sut;

    public PronunciationDictionariesHttpTriggerTests()
    {
        _sut = new PronunciationDictionariesHttpTrigger(
            _dictionaries,
            Substitute.For<IAudioSynthesizer>(),
            Substitute.For<IBroadcastOutput>(),
            Substitute.For<IAudioUrlProvider>(),
            Substitute.For<IAudioProcessor>(),
            Options.Create(new ElevenLabsOptions()),
            Options.Create(new BroadcastOptions()),
            TimeProvider.System,
            Substitute.For<ILogger<PronunciationDictionariesHttpTrigger>>());

        // Default: no existing dictionaries, so duplicate-name tests that don't care about it
        // never hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static PronunciationRuleDto OneRule() => new("alias", "Coomera", "Koomerah");

    private static CreatePronunciationDictionaryRequestDto ValidCreateRequest(
        string name = "Gold Coast", List<PronunciationRuleDto>? rules = null) =>
        new(name, "A description.", rules ?? [OneRule()]);

    private static PronunciationDictionary ExistingDictionary(string name) =>
        PronunciationDictionary.Create(name, null, "pd-eleven", "v1", [], null, null, DateTime.UtcNow).Value;

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest(new string('a', ConfigValidationLimits.PronunciationDictionaryNameMax + 1));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Create_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with
        {
            Description = new string('d', ConfigValidationLimits.PronunciationDictionaryDescriptionMax + 1)
        };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Fact]
    public async Task Create_NoRules_ReturnsBadRequest()
    {
        var request = ValidCreateRequest(rules: []);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("at least one rule");
        await _dictionaries.DidNotReceive().CreateAsync(
            Arg.Any<CreatePronunciationDictionaryInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_AbsentRules_ReturnsBadRequest()
    {
        var request = new CreatePronunciationDictionaryRequestDto("Gold Coast", "A description.", null);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("at least one rule");
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([ExistingDictionary("Gold Coast")]));

        // Case-insensitive match against the existing dictionary's name.
        var request = ValidCreateRequest("gold coast");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("already exists");
        await _dictionaries.DidNotReceive().CreateAsync(
            Arg.Any<CreatePronunciationDictionaryInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ValidRequest_Succeeds()
    {
        var created = ExistingDictionary("Gold Coast");
        _dictionaries.CreateAsync(
                Arg.Any<CreatePronunciationDictionaryInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Success(created));

        var request = ValidCreateRequest();

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_NameTooLong_ReturnsBadRequest()
    {
        var request = new UpdatePronunciationDictionaryRequestDto(
            new string('a', ConfigValidationLimits.PronunciationDictionaryNameMax + 1), "d", [OneRule()]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/pronunciation-dictionaries/pd-1"),
                "pd-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Update_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = new UpdatePronunciationDictionaryRequestDto(
            "Gold Coast", new string('d', ConfigValidationLimits.PronunciationDictionaryDescriptionMax + 1), [OneRule()]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/pronunciation-dictionaries/pd-1"),
                "pd-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Fact]
    public async Task Update_NoRules_StillAllowed()
    {
        // L7 requires >= 1 rule only on the create path; Update is unaffected.
        var existing = ExistingDictionary("Gold Coast");
        _dictionaries.UpdateAsync(
                Arg.Any<string>(), Arg.Any<UpdatePronunciationDictionaryInput>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Success(existing));

        var request = new UpdatePronunciationDictionaryRequestDto("Gold Coast", "d", []);

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/pronunciation-dictionaries/pd-1"),
                "pd-1", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }
    [Fact]
    public async Task Update_RevisionConflict_Returns409()
    {
        var existing = ExistingDictionary("Gold Coast");
        existing.ETag = "etag-v2";
        _dictionaries.UpdateAsync(
                Arg.Any<string>(), Arg.Any<UpdatePronunciationDictionaryInput>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Failure(
                OperationalFailure.Conflict(
                    "pronunciation.revision_mismatch",
                    "The pronunciation dictionary was modified by another request.")));

        var request = new UpdatePronunciationDictionaryRequestDto(
            "Gold Coast", "d", [OneRule()], "edit", "etag-v1");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/pronunciation-dictionaries/pd-1"),
                "pd-1", Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_ServiceRejectsOversizedRules_TypedInvalidInputReturns400()
    {
        // Bug-bash 2026-08-05 finding 6: the over-ceiling single-version refusal used to surface
        // as HTTP 500 because Respond dropped the typed disposition. The typed InvalidInput from
        // the service must map to a 400 with the actionable message.
        _dictionaries.CreateAsync(
                Arg.Any<CreatePronunciationDictionaryInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large",
                "This pronunciation dictionary is too large to save — a single version is 63,956 " +
                "characters against a 32,768 limit. Remove or shorten some entries and save again.")));

        var request = ValidCreateRequest();

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(
                MockHttpRequestData.CreatePost(request, "http://localhost/api/pronunciation-dictionaries"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("too large to save");
    }

}

/// <summary>
///     Preview semantics (bug-bash 2026-08-05 findings 13/14): counts are MEASURED against the
///     sample text with the real word-boundary/case semantics, the model decides which rule types
///     take effect, and a phoneme-carrying dictionary on eleven_multilingual_v2 attaches the
///     alias-only version rather than the whole dictionary.
/// </summary>
public sealed class PronunciationDictionaryPreviewTests
{
    private readonly IPronunciationDictionaryService _dictionaries = Substitute.For<IPronunciationDictionaryService>();
    private readonly IAudioSynthesizer _synthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly PronunciationDictionariesHttpTrigger _sut;

    public PronunciationDictionaryPreviewTests()
    {
        // The preview's post-synthesis plumbing is not under test — stub the audio pipeline so
        // the request completes and the response JSON is readable.
        var broadcastOutput = Substitute.For<IBroadcastOutput>();
        broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var audioUrlProvider = Substitute.For<IAudioUrlProvider>();
        audioUrlProvider.GetAudioUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://audio/out.wav"));
        var audioProcessor = Substitute.For<IAudioProcessor>();
        audioProcessor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);

        _sut = new PronunciationDictionariesHttpTrigger(
            _dictionaries,
            _synthesizer,
            broadcastOutput,
            audioUrlProvider,
            audioProcessor,
            Options.Create(new ElevenLabsOptions()),
            Options.Create(new BroadcastOptions()),
            TimeProvider.System,
            Substitute.For<ILogger<PronunciationDictionariesHttpTrigger>>());

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(new byte[8])));
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static PronunciationRule Alias(string word, string alias) =>
        PronunciationRule.CreateAlias(word, alias).Value;

    private static PronunciationRule Phoneme(string word) =>
        PronunciationRule.CreatePhoneme(word, "ˈlɒklən", "ipa").Value;

    private static PronunciationDictionary DictionaryWith(
        IReadOnlyList<PronunciationRule> rules, string? aliasOnlyVersionId = null) =>
        PronunciationDictionary.Create(
            "Melbourne", null, "el-dict-1", "full-v1", rules, "init", "operator", DateTime.UtcNow,
            "pd-1", aliasOnlyVersionId: aliasOnlyVersionId).Value;

    private void SetupDictionary(PronunciationDictionary dictionary) =>
        _dictionaries.GetAsync("pd-1", Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Success(dictionary));

    private async Task<PronunciationDictionaryPreviewResponseDto> Preview(string model, string sample)
    {
        var response = await _sut.Preview(
            MockHttpRequestData.CreatePost(new PreviewPronunciationRequestDto(Model: model, SampleText: sample),
                "http://localhost/api/pronunciation-dictionaries/pd-1/preview"),
            "pd-1", Ctx());
        var result = await MockHttpRequestData.ReadResponseJsonAsync<PronunciationDictionaryPreviewResponseDto>(response);
        result.ShouldNotBeNull();
        return result!;
    }

    [Fact]
    public async Task Preview_CountsAreMeasuredAgainstTheSampleText()
    {
        // Finding 14: the probe dictionary (four alias rules + one phoneme rule) reported four
        // rules "applied" even for a sample containing NONE of the corrected words. Counts must
        // be measured against the sample, not computed from rule types.
        SetupDictionary(DictionaryWith(
            [Alias("Cairns", "Cans"), Alias("St Kilda", "Saint Kilda"), Alias("Coomera", "Koomera"),
                Alias("Warringah", "Warringa"), Phoneme("Lachlan")]));

        var result = await Preview("eleven_v3", "Good morning, traffic is flowing freely on the M2 today.");

        result.AppliedRuleCount.ShouldBe(0);
        result.SkippedRuleCount.ShouldBe(5);
    }

    [Fact]
    public async Task Preview_SampleContainingRuleWords_CountsOnlyMatchingSupportedRules()
    {
        SetupDictionary(DictionaryWith([Alias("Cairns", "Cans"), Alias("Coomera", "Koomera"), Phoneme("Lachlan")]));

        // v3 skips phoneme rules (benign), so only the two matching alias rules apply.
        var result = await Preview("eleven_v3", "Expect delays around Cairns and Coomera today.");

        result.AppliedRuleCount.ShouldBe(2);
        result.SkippedRuleCount.ShouldBe(1);
    }

    [Fact]
    public async Task Preview_PhonemeRule_OnTurboV2_CountsAsApplied()
    {
        // Turbo/flash v2 renders phoneme rules — a matching phoneme rule is applied, not skipped.
        SetupDictionary(DictionaryWith([Alias("Cairns", "Cans"), Phoneme("Lachlan")]));

        var result = await Preview("eleven_turbo_v2", "The Lachlan interchange and Cairns are both clear.");

        result.AppliedRuleCount.ShouldBe(2);
        result.SkippedRuleCount.ShouldBe(0);
    }

    [Fact]
    public async Task Preview_PhonemeRule_OnMultilingualV2_SkippedAndAliasOnlyAttached()
    {
        // Findings 13(a)+13(b): eleven_multilingual_v2 is NOT v3, so the old substring test
        // counted its phoneme rule as "applied" on a model whose documented effect is deleting
        // matched words. The preview must count it as skipped AND attach the alias-only version
        // so the preview audio cannot delete words either — the same decision the airtime
        // resolver makes, via PronunciationDictionary.LocatorFor.
        SetupDictionary(DictionaryWith(
            [Alias("Warringah", "Warringa"), Phoneme("Lachlan")], aliasOnlyVersionId: "alias-v1"));

        PronunciationStack? captured = null;
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(ci =>
            {
                captured = ci.ArgAt<PronunciationStack?>(6);
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(new byte[8]));
            });

        var result = await Preview(
            "eleven_multilingual_v2", "Warringah Road and the Lachlan interchange are busy.");

        result.AppliedRuleCount.ShouldBe(1); // the Warringah alias only
        result.SkippedRuleCount.ShouldBe(1); // the Lachlan phoneme rule would delete its word here
        captured.ShouldNotBeNull();
        captured!.Station.HasValue.ShouldBeTrue();
        captured.Station.Value.DictionaryId.ShouldBe("el-dict-1");
        captured.Station.Value.VersionId.ShouldBe("alias-v1");
    }

    [Fact]
    public async Task Preview_PhonemeRule_OnMultilingualV2_WithoutAliasOnlyVersion_AttachesNothing()
    {
        // A phoneme-carrying dictionary with no alias-only version must NOT attach the full
        // dictionary on multilingual_v2 (it would delete matched words); the layer is left off.
        SetupDictionary(DictionaryWith([Alias("Warringah", "Warringa"), Phoneme("Lachlan")]));

        PronunciationStack? captured = null;
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(ci =>
            {
                captured = ci.ArgAt<PronunciationStack?>(6);
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(new byte[8]));
            });

        var result = await Preview(
            "eleven_multilingual_v2", "Warringah Road and the Lachlan interchange are busy.");

        result.AppliedRuleCount.ShouldBe(1);
        result.SkippedRuleCount.ShouldBe(1);
        captured.ShouldNotBeNull();
        captured!.Station.HasValue.ShouldBeFalse();
    }
}
