using System.Globalization;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The shared pronunciation-dictionary library: named, versioned ElevenLabs dictionaries
///     (suburb/road alias + phoneme rules) stations read with, so different regions carry their
///     own pronunciations and ARN can evolve them. CRUD + version restore delegate to
///     <see cref="IPronunciationDictionaryService" />; preview attaches a saved dictionary to a
///     sample synthesis on a chosen model so an operator can hear the effect and see which rules
///     a model applies (alias on every model; phoneme on the v2-family only — v3 skips phoneme).
///     The id-less preview-rule route auditions ONE candidate rule (saved or not) in a chosen
///     voice: alias by reading the replacement verbatim, phoneme by an inline tag (v3
///     slash-wrapped IPA / eleven_flash_v2 SSML <phoneme>) — no dictionary needed.
///     Mirrors the voice-profile / delivery-style libraries.
/// </summary>
public sealed class PronunciationDictionariesHttpTrigger(
    IPronunciationDictionaryService dictionaries,
    IAudioSynthesizer audioSynthesizer,
    IBroadcastOutput broadcastOutput,
    IAudioUrlProvider audioUrlProvider,
    IAudioProcessor audioProcessor,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    ILogger<PronunciationDictionariesHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    // A sample that exercises tricky Australian place names so a preview audibly demonstrates rules.
    private const string PreviewScript =
        "Good morning — here is your traffic update. Expect delays around Warringah Road and the " +
        "Lachlan interchange, with the run through Coomera flowing freely. Drive safely out there.";

    [Function("ListPronunciationDictionaries")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "pronunciation-dictionaries")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var query = VersionedLibraryListQueryParser.Parse(req);
        if (query.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, query.Error);

        var result = await dictionaries.ListAsync(query.Value, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var items = result.Value.Items.Select(ToDto).ToList();
        if (!query.Value.IsPaged)
            return await HttpResponseHelper.OkJsonAsync(req,
                new PronunciationDictionariesListResponseDto(items, result.Value.Total));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = items,
            Total = result.Value.Total,
            NextCursor = result.Value.NextCursor
        });
    }

    [Function("CreatePronunciationDictionary")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "pronunciation-dictionaries")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<CreatePronunciationDictionaryRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.PronunciationDictionaryNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.PronunciationDictionaryNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.PronunciationDictionaryDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.PronunciationDictionaryDescriptionMax} characters.");
        if (dto.Rules is null || dto.Rules.Count == 0)
            return await HttpResponseHelper.BadRequestAsync(req,
                "A pronunciation dictionary must have at least one rule.");

        var rules = PronunciationRuleMapping.ParseRules(dto.Rules);
        if (rules.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, rules.Error);

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var existingForDuplicateCheck = await dictionaries.ListAsync(ctx.CancellationToken);
            if (existingForDuplicateCheck.IsSuccess && existingForDuplicateCheck.Value.Any(d =>
                    string.Equals(d.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
                return await HttpResponseHelper.ConflictAsync(req,
                    $"A pronunciation dictionary named '{dto.Name}' already exists.");
        }

        if (!PronunciationRuleMapping.TryParseScope(dto.Scope, out var scope))
            return await HttpResponseHelper.BadRequestAsync(req, PronunciationRuleMapping.UnrecognisedScope(dto.Scope));

        var input = new CreatePronunciationDictionaryInput(dto.Name ?? string.Empty, dto.Description, rules.Value,
            dto.ChangeNote, Scope: scope);
        var result = await dictionaries.CreateAsync(input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("GetPronunciationDictionary")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "pronunciation-dictionaries/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A pronunciation dictionary id is required.");

        var result = await dictionaries.GetAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UpdatePronunciationDictionary")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "pronunciation-dictionaries/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A pronunciation dictionary id is required.");

        var parse = await HttpRequestParser.ParseAsync<UpdatePronunciationDictionaryRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.PronunciationDictionaryNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.PronunciationDictionaryNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.PronunciationDictionaryDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.PronunciationDictionaryDescriptionMax} characters.");
        var rules = PronunciationRuleMapping.ParseRules(dto.Rules);
        if (rules.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, rules.Error);

        if (!PronunciationRuleMapping.TryParseScope(dto.Scope, out var scope))
            return await HttpResponseHelper.BadRequestAsync(req, PronunciationRuleMapping.UnrecognisedScope(dto.Scope));

        var input = new UpdatePronunciationDictionaryInput(dto.Name ?? string.Empty, dto.Description, rules.Value,
            dto.ChangeNote, dto.ExpectedEtag, scope);
        var result = await dictionaries.UpdateAsync(id, input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("RestorePronunciationDictionaryVersion")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "pronunciation-dictionaries/{id}/versions/{version}/restore")]
        HttpRequestData req,
        string id, string version, FunctionContext ctx)
    {
        if (!int.TryParse(version, out var versionNumber))
            return await HttpResponseHelper.BadRequestAsync(req, "Version must be an integer.");

        var result = await dictionaries.RestoreVersionAsync(id, versionNumber, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("ArchivePronunciationDictionary")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "pronunciation-dictionaries/{id}/archive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A pronunciation dictionary id is required.");

        var result = await dictionaries.ArchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UnarchivePronunciationDictionary")]
    public async Task<HttpResponseData> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "pronunciation-dictionaries/{id}/unarchive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A pronunciation dictionary id is required.");

        var result = await dictionaries.UnarchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("DeletePronunciationDictionary")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "pronunciation-dictionaries/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A pronunciation dictionary id is required.");

        var result = await dictionaries.DeleteAsync(id, ctx.CancellationToken);
        if (result.IsFailure)
            // Typed mapping: an in-use item returns a Conflict whose message names the
            // stations, and message matching alone would send that to a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        return HttpResponseHelper.NoContent(req);
    }

    [Function("PreviewPronunciationDictionary")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "pronunciation-dictionaries/{id}/preview")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var existing = await dictionaries.GetAsync(id, ct);
        if (existing.IsFailure)
            return await Respond(req, existing);

        var dictionary = existing.Value;
        if (dictionary.CurrentLocator.IsNone)
            return await HttpResponseHelper.BadRequestAsync(req,
                "This dictionary has no usable ElevenLabs version to preview.");

        var parse = await HttpRequestParser.ParseAsync<PreviewPronunciationRequestDto>(req, ct);
        var dto = parse.IsSuccess ? parse.Value : new PreviewPronunciationRequestDto();

        var model = string.IsNullOrWhiteSpace(dto.Model) ? elevenLabsOptions.Value.ModelId : dto.Model!;
        var voiceId = string.IsNullOrWhiteSpace(dto.BaseVoiceId) ? broadcastOptions.Value.DefaultVoiceId : dto.BaseVoiceId!;
        var sample = string.IsNullOrWhiteSpace(dto.SampleText) ? PreviewScript : dto.SampleText!;

        // The counts are MEASURED, not computed: a rule counts as applied only when its word
        // actually appears in the sample (same word-boundary/case semantics the real substitution
        // uses — see PronunciationRule.MatchesIn) AND the model applies its type (alias on every
        // model; phoneme on the turbo/flash v2 family only — v3 skips them, multilingual_v2 would
        // delete matched words, see PronunciationRule.AppliesOn). Skipped = the rest.
        int appliedCount = 0;
        var skippedDetails = new List<string>();
        foreach (var rule in dictionary.CurrentRules)
        {
            bool matched = rule.MatchesIn(sample);
            bool applies = rule.AppliesOn(model);
            if (matched && applies)
            {
                appliedCount++;
                continue;
            }

            skippedDetails.Add(matched
                ? $"'{rule.StringToReplace}' matches the sample but {model} does not apply " +
                  $"{rule.Type.ToString().ToLowerInvariant()} rules"
                : $"'{rule.StringToReplace}' does not appear in the sample");
        }
        int skippedCount = dictionary.CurrentRules.Count - appliedCount;

        // The preview must attach exactly what would air for this model: a phoneme-carrying
        // dictionary on eleven_multilingual_v2 would delete matched words from the preview audio,
        // so the alias-only version is attached instead (the same single decision the airtime
        // resolver uses — PronunciationDictionary.LocatorFor — so they can never disagree).
        var stationLocator = dictionary.LocatorFor(model);
        var droppedWords = dictionary.UnsupportedRuleWordsFor(model);
        if (droppedWords.Count > 0)
        {
            logger.LogWarning(
                "[PronunciationPreview] Dictionary {Id} on {Model}: phoneme rule(s) ({Words}) would be " +
                "dropped by this model, so the preview attaches the alias-only version",
                id, model, string.Join(", ", droppedWords));
        }

        // Auditioning the previewed dictionary alone is not what airs when a platform-wide layer is
        // also active, so the operator can opt into hearing the real stack. Only meaningful for a
        // station dictionary: previewing the platform-wide one already IS the global layer.
        var globalLocator = Option<PronunciationLocator>.None;
        if (dto.IncludeGlobalLayer && !dictionary.IsGlobal)
        {
            var library = await dictionaries.ListAsync(ct);
            if (library.IsSuccess)
            {
                var activeGlobal = library.Value.FirstOrDefault(d => d.IsGlobal && d.IsActive);
                if (activeGlobal is not null)
                    globalLocator = activeGlobal.LocatorFor(model);
            }
        }

        // The counts describe the PREVIEWED dictionary only — folding the platform-wide layer's
        // rules in would make "12 rules applied" mean something different per toggle state.
        logger.LogInformation(
            "[PronunciationPreview] Dictionary {Id} on {Model} (voice {VoiceId}) — {Applied} applied, " +
            "{Skipped} skipped ({SkippedDetails}), platform-wide layer {GlobalState}",
            id, model, voiceId, appliedCount, skippedCount, string.Join("; ", skippedDetails),
            globalLocator.HasValue ? "included" : "not included");
        string dictionaryKey =
            $"station={LocatorKey(stationLocator)}|global={LocatorKey(globalLocator)}";
        var key = new InteractiveWorkKey(
            PrincipalKey(req),
            "pronunciation-dictionary-preview",
            $"{dictionary.Id}:v{dictionary.CurrentVersion}",
            $"{sample}\u001fmodel={model}\u001fvoice={voiceId}\u001fincludeGlobal={dto.IncludeGlobalLayer}",
            model,
            string.Empty,
            dictionaryKey,
            string.Empty);
        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            key,
            async operationCt =>
            {
                var synthResult = await audioSynthesizer.SynthesizeAsync(
                    sample, Option<VoiceSettings>.None, Option<string>.Some(voiceId), Option<string>.Some(model),
                    operationCt, Option<string>.None,
                    PronunciationStack.Of(stationLocator, globalLocator));
                if (synthResult.IsFailure)
                    return synthResult.PropagateFailure<PronunciationDictionaryPreviewResponseDto>();

                var audio = synthResult.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                var fileName = $"pronunciationpreview_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var save = await broadcastOutput.SaveAsync(audio, fileName, operationCt);
                if (save.IsFailure)
                    return save.PropagateFailure<PronunciationDictionaryPreviewResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var urlResult = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                var duration = audioProcessor.GetDuration(audio);

                return Result<PronunciationDictionaryPreviewResponseDto>.Success(
                    new PronunciationDictionaryPreviewResponseDto(
                        urlResult.IsSuccess ? urlResult.Value : string.Empty,
                        model,
                        appliedCount,
                        skippedCount,
                        Math.Round(duration),
                        globalLocator.HasValue));
            },
            ct);

        if (operation.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(req, operation.Error,
                operation.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    // Distinct literal route so it always wins over pronunciation-dictionaries/{id}: a
    // candidate-rule preview has no id (the rule may not be saved yet), and the route must
    // not be parsed as one.
    [Function("PreviewPronunciationRule")]
    public async Task<HttpResponseData> PreviewRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "pronunciation-dictionaries/preview-rule")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var parse = await HttpRequestParser.ParseAsync<PreviewPronunciationRuleRequestDto>(req, ct);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;

        // The candidate goes through the SAME construction path a save uses
        // (PronunciationRuleMapping.ParseRules is the single choke point for the studio
        // editor, the review-cockpit quick-fix and config import), so preview and save can
        // never disagree about what counts as a rule — a rule that would fail the save cannot
        // audition, and the rejection is the domain's own, naming the word.
        var rules = PronunciationRuleMapping.ParseRules(new List<PronunciationRuleDto>
        {
            new(Type: dto.Type, StringToReplace: dto.StringToReplace, Replacement: dto.Replacement,
                Alphabet: dto.Alphabet),
        });
        if (rules.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, rules.Error);
        var rule = rules.Value[0];

        // The whole point of a per-rule preview is hearing a CHOSEN voice, so (unlike the
        // whole-dictionary preview) there is no default fallback: a missing BaseVoiceId is an
        // operator omission, not a configuration gap, and silently substituting the platform
        // default would teach the wrong voice.
        if (string.IsNullOrWhiteSpace(dto.BaseVoiceId))
            return await HttpResponseHelper.BadRequestAsync(req,
                "A base voice id is required to preview a rule — pick a voice in the Rules tab.");

        var model = string.IsNullOrWhiteSpace(dto.Model) ? elevenLabsOptions.Value.ModelId : dto.Model.Trim();

        string spokenText;
        bool phonemeApplied;
        if (rule.Type == PronunciationRuleType.Alias)
        {
            // An alias substitutes the word with the replacement, which is then spoken as
            // ordinary words — the replacement IS the text that airs, verbatim, on every model.
            spokenText = rule.Replacement;
            phonemeApplied = false;
        }
        else
        {
            // Speaking raw IPA/ARPAbet would read the phonemes as words. The tag rides in the
            // text instead; the model policy is PronunciationRule.BuildInlinePhonemeText's.
            var tagged = PronunciationRule.BuildInlinePhonemeText(
                model, rule.Alphabet ?? "ipa", rule.StringToReplace, rule.Replacement);
            if (tagged.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, tagged.Error);
            spokenText = tagged.Value;
            phonemeApplied = true;
        }

        logger.LogInformation(
            "[PronunciationRulePreview] Auditioning {Type} rule '{Word}' on {Model} (voice {VoiceId}) — " +
            "phoneme applied: {PhonemeApplied}",
            rule.Type.ToString().ToLowerInvariant(), rule.StringToReplace, model, dto.BaseVoiceId,
            phonemeApplied);

        var ruleKey = string.Join(
            "\u001f",
            rule.Type.ToString(),
            rule.StringToReplace,
            rule.Replacement,
            rule.Alphabet ?? string.Empty);
        var key = new InteractiveWorkKey(
            PrincipalKey(req),
            "pronunciation-rule-preview",
            ruleKey,
            $"{spokenText}\u001fvoice={dto.BaseVoiceId}",
            model,
            string.Empty,
            string.Empty,
            string.Empty);
        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            key,
            async operationCt =>
            {
                var synthResult = await audioSynthesizer.SynthesizeAsync(
                    spokenText, Option<VoiceSettings>.None, Option<string>.Some(dto.BaseVoiceId!),
                    Option<string>.Some(model), operationCt, Option<string>.None);
                if (synthResult.IsFailure)
                    return synthResult.PropagateFailure<PronunciationRulePreviewResponseDto>();

                var audio = synthResult.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                var fileName = $"pronunciationrulepreview_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var save = await broadcastOutput.SaveAsync(audio, fileName, operationCt);
                if (save.IsFailure)
                    return save.PropagateFailure<PronunciationRulePreviewResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var urlResult = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                var duration = audioProcessor.GetDuration(audio);

                return Result<PronunciationRulePreviewResponseDto>.Success(
                    new PronunciationRulePreviewResponseDto(
                        urlResult.IsSuccess ? urlResult.Value : string.Empty,
                        duration,
                        spokenText,
                        phonemeApplied));
            },
            ct);

        if (operation.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(req, operation.Error,
                operation.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    private static async Task<HttpResponseData> Respond(HttpRequestData req, Result<PronunciationDictionary> result)
    {
        if (result.IsFailure)
            // Typed mapping first: an InvalidInput (e.g. an oversized version refused by the
            // storage pre-flight) must be a 400 and a Conflict (revision mismatch, global in use)
            // a 409, regardless of how the message happens to read — message matching alone
            // drops the disposition and sends client-input errors to a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) =>
                    e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                    e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase) ||
                    e.Contains("must be", StringComparison.OrdinalIgnoreCase)
                        ? HttpResponseHelper.BadRequestAsync(r, e)
                        : HttpResponseHelper.InternalErrorAsync(r, e));

        return await HttpResponseHelper.OkJsonAsync(req, ToDto(result.Value));
    }

    internal static PronunciationRuleDto ToRuleDto(PronunciationRule r) =>
        new(r.Type == PronunciationRuleType.Phoneme ? "phoneme" : "alias",
            r.StringToReplace, r.Replacement, r.Alphabet, r.CaseSensitive, r.WordBoundaries, r.AppliesOnAllModels);

    internal static PronunciationDictionaryDto ToDto(PronunciationDictionary d)
    {
        var versions = d.Versions.Select(v => new PronunciationDictionaryVersionDto(
            v.Version,
            v.ElevenLabsVersionId,
            v.Rules.Select(ToRuleDto).ToList(),
            v.ChangeNote,
            v.CreatedAt.ToString("o"),
            v.CreatedBy)).ToList();

        return new PronunciationDictionaryDto(
            d.Id,
            d.DictionaryId,
            d.Name,
            d.Description,
            d.Status,
            d.Scope.ToString(),
            d.CurrentVersion,
            d.CreatedAt.ToString("o"),
            d.UpdatedAt.ToString("o"),
            d.ETag,
            d.CreatedBy,
            d.AssignedStations.ToList(),
            d.CurrentRules.Select(ToRuleDto).ToList(),
            versions);
    }
    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

    private static string LocatorKey(Option<PronunciationLocator> locator) =>
        locator.HasValue ? $"{locator.Value.DictionaryId}:{locator.Value.VersionId}" : string.Empty;

}

// ─── Request DTOs (arrive PascalCase — the SPA pascalizes bodies before sending) ───

public sealed record CreatePronunciationDictionaryRequestDto(
    string? Name = null,
    string? Description = null,
    List<PronunciationRuleDto>? Rules = null,
    string? ChangeNote = null,
    string? Scope = null);

public sealed record UpdatePronunciationDictionaryRequestDto(
    string? Name = null,
    string? Description = null,
    List<PronunciationRuleDto>? Rules = null,
    string? ChangeNote = null,
    string? ExpectedEtag = null,
    string? Scope = null);

public sealed record PreviewPronunciationRequestDto(
    string? BaseVoiceId = null,
    string? Model = null,
    string? SampleText = null,
    bool IncludeGlobalLayer = false);

/// <summary>
///     Body for previewing ONE candidate rule (saved or not) in a chosen voice. Unlike the
///     whole-dictionary preview there is no dictionary id in the route: the rule is validated
///     through the same domain construction a save uses and synthesized directly, so an
///     operator can hear a rule they have just typed. <see cref="BaseVoiceId" /> is required —
///     the entire point is hearing a chosen voice.
/// </summary>
public sealed record PreviewPronunciationRuleRequestDto(
    string? Type = null,
    string? StringToReplace = null,
    string? Replacement = null,
    string? Alphabet = null,
    string? BaseVoiceId = null,
    string? Model = null);

// ─── Response DTOs (PascalCase; the SPA's Zod schemas camelize on receipt) ───

public sealed record PronunciationDictionaryVersionDto(
    int Version,
    string ElevenLabsVersionId,
    IReadOnlyList<PronunciationRuleDto> Rules,
    string? ChangeNote,
    string CreatedAt,
    string? CreatedBy);

public sealed record PronunciationDictionaryDto(
    string Id,
    string DictionaryId,
    string Name,
    string? Description,
    string Status,
    string Scope,
    int CurrentVersion,
    string CreatedAt,
    string UpdatedAt,
    string? Etag,
    string? CreatedBy,
    IReadOnlyList<string> AssignedStations,
    IReadOnlyList<PronunciationRuleDto> Rules,
    IReadOnlyList<PronunciationDictionaryVersionDto> Versions);

public sealed record PronunciationDictionariesListResponseDto(
    IReadOnlyList<PronunciationDictionaryDto> Items,
    int Total);

public sealed record PronunciationDictionaryPreviewResponseDto(
    string Url,
    string Model,
    int AppliedRuleCount,
    int SkippedRuleCount,
    double EstimatedDurationSec,
    bool GlobalLayerApplied = false);

public sealed record PronunciationRulePreviewResponseDto(
    string Url,
    double DurationSeconds,
    string SpokenText,
    bool PhonemeApplied);
