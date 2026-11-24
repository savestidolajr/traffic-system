using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;

/// <summary>
///     HTTP client for ElevenLabs Text-to-Speech API.
///     Implements IVoiceProvider for voice listing abstraction.
/// </summary>
public sealed class ElevenLabsClient(
    HttpClient httpClient,
    IOptions<ElevenLabsOptions> options,
    ILogger<ElevenLabsClient> logger,
    ElevenLabsTtsBudget? ttsBudget = null) : IVoiceProvider, IVoiceRemixer, IPronunciationDictionaryProvider
{
    // Audio format constants — speed clamp is sourced from ElevenLabsOptions; see ClampSpeed.
    private const int DefaultSampleRate = 16000;
    private const int DefaultMp3Bitrate = 192_000;
    private const int BitsPerByte = 8;
    private const int BytesPerSample16Bit = 2;
    private const int MonoChannels = 1;
    private const int BitsPerSample16 = 16;

    private readonly ElevenLabsTtsBudget _ttsBudget = ttsBudget ?? new ElevenLabsTtsBudget();
    private readonly ElevenLabsOptions _options = options.Value;
    private bool HasApiKey => !string.IsNullOrEmpty(_options.ApiKey);
    private bool UseTimestampsEndpoint => _options.UseTimestampsEndpoint;

    private bool IsPcmFormat =>
        _options.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase);

    // =========================================================================
    // IVoiceProvider Implementation
    // =========================================================================

    /// <summary>
    ///     Gets all available voices as domain Voice models.
    /// </summary>
    async Task<Result<IReadOnlyList<Voice>>> IVoiceProvider.GetVoicesAsync(CancellationToken ct)
    {
        var result = await GetVoicesAsync(ct);
        if (result.IsFailure)
            return Result<IReadOnlyList<Voice>>.Failure(result.Error);

        var voices = result.Value.Select(v => new Voice(
                v.VoiceId,
                v.Name,
                string.IsNullOrWhiteSpace(v.Category) ? Option<string>.None : Option<string>.Some(v.Category),
                string.IsNullOrWhiteSpace(v.Description) ? Option<string>.None : Option<string>.Some(v.Description),
                v.Labels ?? new Dictionary<string, string>())
        { ModelIds = v.ModelIds, CanRemix = v.CanRemix }).ToList();

        return Result<IReadOnlyList<Voice>>.Success(voices);
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    ///     Fetches all available voices from ElevenLabs.
    /// </summary>
    public async Task<Result<IReadOnlyList<ElevenLabsVoice>>> GetVoicesAsync(CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<IReadOnlyList<ElevenLabsVoice>>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);

        try
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Get, $"{_options.ApiBaseUrl}/{_options.VoicesEndpoint}");
            request.Headers.Add("xi-api-key", _options.ApiKey);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("ElevenLabs voices API failed. StatusCode: {StatusCode}, Response: {Error}",
                    response.StatusCode, error);
                return Result<IReadOnlyList<ElevenLabsVoice>>.Failure($"Failed to fetch voices: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<VoicesResponse>(ct);
            var voices = data?.Voices.Select(v => v.ToModel()).ToList() ?? [];

            logger.LogInformation("Fetched {Count} voices from ElevenLabs", voices.Count);
            return Result<IReadOnlyList<ElevenLabsVoice>>.Success(voices);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching ElevenLabs voices");
            return Result<IReadOnlyList<ElevenLabsVoice>>.Failure($"Error fetching voices: {ex.Message}");
        }
    }

    /// <summary>
    ///     Generates audio from text using ElevenLabs TTS.
    /// </summary>
    public async Task<Result<ElevenLabsAudioResult>> GenerateAudioAsync(
        string text,
        double speed,
        double stability,
        double similarityBoost,
        double style,
        bool useSpeakerBoost,
        string voiceId,
        string? modelOverride = null,
        string? market = null,
        CancellationToken ct = default,
        PronunciationStack? pronunciationStack = null)
    {
        if (!HasApiKey)
            return Result<ElevenLabsAudioResult>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);

        if (string.IsNullOrEmpty(voiceId))
            return Result<ElevenLabsAudioResult>.Failure(
                "VoiceId is required. Each schedule or job must specify a voice.");

        string modelId = modelOverride ?? _options.ModelId;
        speed = ClampSpeed(speed);

        string endpointSuffix = UseTimestampsEndpoint ? "/with-timestamps" : "";
        string url = $"{_options.BaseUrl.TrimEnd('/')}/{voiceId}{endpointSuffix}?output_format={_options.OutputFormat}";
        object payload = BuildTtsPayload(text, speed, stability, similarityBoost, style, useSpeakerBoost, modelId, market, pronunciationStack);

        logger.LogInformation(
            "ElevenLabs TTS - Voice: {VoiceId}, Model: {ModelId}, Speed: {Speed:F2}, TextLength: {TextLength}",
            voiceId, modelId, speed, text.Length);

        try
        {
            using var lease = await _ttsBudget.AcquireAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, lease.Token);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(lease.Token);
                logger.LogError(
                    "ElevenLabs TTS failed. Voice: {VoiceId}, Model: {ModelId}, StatusCode: {StatusCode}, Response: {Error}",
                    voiceId, modelId, response.StatusCode, error);
                // The raw provider body (incl. request_id) stays in the log above and NEVER
                // reaches a client — surface the human-readable detail.message instead, same as
                // the remix path. A 4xx is operator-correctable input (unknown/ineligible
                // voice, bad settings) → InvalidInput so the HTTP edge returns 400; anything
                // else is a provider-side fault → retryable. 401/403 are auth/config faults on
                // OUR side (bad/missing API key, revoked subscription) — NOT the caller's
                // input — so they must not surface as a client-error 400 and stay retryable
                // like other provider faults.
                string sanitized = $"TTS failed: {FormatElevenLabsError(response.StatusCode, error)}";
                int statusCode = (int)response.StatusCode;
                var failure = statusCode is >= 400 and < 500 && statusCode is not 401 and not 403
                    ? OperationalFailure.InvalidInput("tts.invalid_request", sanitized)
                    : OperationalFailure.Retryable("tts.provider_error", sanitized);
                return Result<ElevenLabsAudioResult>.Failure(failure);
            }

            return UseTimestampsEndpoint
                ? await ParseTimestampsResponse(response, lease.Token)
                : await ParseAudioResponse(response, lease.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ElevenLabs TTS request failed. Voice: {VoiceId}, Model: {ModelId}, TextLength: {TextLength}",
                voiceId, modelId, text.Length);
            return Result<ElevenLabsAudioResult>.Failure(ex.Message);
        }
    }

    // =========================================================================
    // Voice Remixing (Text to Voice)
    // =========================================================================

    /// <summary>
    ///     Remixes an existing voice from a natural-language description, returning previews of
    ///     the remixed voice reading the sample script. The audio is finalized to the configured
    ///     output format (same pipeline as TTS), so previews are playable as-is.
    /// </summary>
    public async Task<Result<IReadOnlyList<VoiceRemixPreview>>> RemixVoiceAsync(
        string voiceId,
        string voiceDescription,
        string previewText,
        double? promptStrength,
        CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<IReadOnlyList<VoiceRemixPreview>>.Failure(
                ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(voiceId))
            return Result<IReadOnlyList<VoiceRemixPreview>>.Failure("VoiceId is required to remix a voice.");
        if (string.IsNullOrWhiteSpace(voiceDescription))
            return Result<IReadOnlyList<VoiceRemixPreview>>.Failure(
                "A voice description is required to remix a voice.");

        var payload = new Dictionary<string, object>
        {
            ["voice_description"] = voiceDescription,
            ["text"] = previewText
        };
        if (promptStrength is { } strength)
            payload["prompt_strength"] = Math.Clamp(strength, 0.0, 1.0);

        string url = $"{_options.ApiBaseUrl}/text-to-voice/{voiceId}/remix?output_format={_options.OutputFormat}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError(
                    "ElevenLabs voice remix failed. Voice: {VoiceId}, StatusCode: {StatusCode}, Response: {Error}",
                    voiceId, response.StatusCode, error);
                return Result<IReadOnlyList<VoiceRemixPreview>>.Failure(
                    FormatElevenLabsError(response.StatusCode, error));
            }

            var data = await response.Content.ReadFromJsonAsync<VoiceRemixResponse>(ct);
            if (data is null || data.Previews.Count == 0)
                return Result<IReadOnlyList<VoiceRemixPreview>>.Failure("Voice remix returned no previews.");

            var previews = new List<VoiceRemixPreview>(data.Previews.Count);
            foreach (var p in data.Previews)
            {
                byte[] raw = Convert.FromBase64String(p.AudioBase64);
                (byte[] outputBytes, double estimated) = FinalizeAudio(raw);
                previews.Add(new VoiceRemixPreview(p.GeneratedVoiceId, outputBytes,
                    p.DurationSecs > 0 ? p.DurationSecs : estimated));
            }

            logger.LogInformation("Remixed voice {VoiceId} — {Count} preview(s) generated", voiceId, previews.Count);
            return Result<IReadOnlyList<VoiceRemixPreview>>.Success(previews);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error remixing ElevenLabs voice {VoiceId}", voiceId);
            return Result<IReadOnlyList<VoiceRemixPreview>>.Failure($"Error remixing voice: {ex.Message}");
        }
    }

    /// <summary>
    ///     Mints a permanent voice from a remix preview's generated_voice_id and returns its
    ///     voice id. Must be called after <see cref="RemixVoiceAsync" /> produced the preview.
    /// </summary>
    public async Task<Result<string>> CreateVoiceFromPreviewAsync(
        string generatedVoiceId,
        string voiceName,
        string voiceDescription,
        CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<string>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(generatedVoiceId))
            return Result<string>.Failure("A generated voice id is required to create a voice.");

        var payload = new Dictionary<string, object>
        {
            ["voice_name"] = string.IsNullOrWhiteSpace(voiceName) ? "Remixed voice" : voiceName,
            ["voice_description"] = string.IsNullOrWhiteSpace(voiceDescription) ? "Remixed voice" : voiceDescription,
            ["generated_voice_id"] = generatedVoiceId
        };

        string url = $"{_options.ApiBaseUrl}/text-to-voice";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("ElevenLabs create-voice failed. StatusCode: {StatusCode}, Response: {Error}",
                    response.StatusCode, error);
                return Result<string>.Failure(FormatElevenLabsError(response.StatusCode, error));
            }

            var data = await response.Content.ReadFromJsonAsync<CreateVoiceResponse>(ct);
            if (data is null || string.IsNullOrWhiteSpace(data.VoiceId))
                return Result<string>.Failure("Create-voice response did not include a voice id.");

            logger.LogInformation("Created remixed voice {VoiceId} ({Name})", data.VoiceId, data.Name);
            return Result<string>.Success(data.VoiceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating remixed ElevenLabs voice from preview {GeneratedVoiceId}",
                generatedVoiceId);
            return Result<string>.Failure($"Error creating voice: {ex.Message}");
        }
    }

    /// <summary>
    ///     Deletes a voice minted from a remix preview (DELETE <c>/voices/{voiceId}</c>). Used to
    ///     roll back an upstream mint whose local record could not be persisted (an otherwise
    ///     billed orphan). Idempotent: a 404 (voice already gone) is treated as success, mirroring
    ///     <see cref="ArchiveAsync" />.
    /// </summary>
    public async Task<Result> DeleteVoiceAsync(string voiceId, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(voiceId))
            return Result.Failure("A voice id is required to delete.");

        string url = $"{_options.ApiBaseUrl}/{_options.VoicesEndpoint}/{voiceId}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Success(); // already gone — nothing left to delete

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("ElevenLabs delete voice failed. StatusCode: {StatusCode}, Response: {Error}",
                    response.StatusCode, error);
                return Result.Failure(FormatElevenLabsError(response.StatusCode, error));
            }

            logger.LogInformation("ElevenLabs delete voice ok — VoiceId={VoiceId}", voiceId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting ElevenLabs remixed voice {VoiceId}", voiceId);
            return Result.Failure($"Error deleting voice: {ex.Message}");
        }
    }

    // =========================================================================
    // IPronunciationDictionaryProvider Implementation (dictionary management)
    // =========================================================================

    /// <summary>
    ///     Creates a new ElevenLabs pronunciation dictionary seeded with the supplied rules
    ///     (add-from-rules) and returns its dictionary id + initial version id.
    /// </summary>
    public async Task<Result<PronunciationLocator>> CreateAsync(
        string name, IReadOnlyList<PronunciationRule> rules, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<PronunciationLocator>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(name))
            return Result<PronunciationLocator>.Failure("A dictionary name is required.");

        var payload = new Dictionary<string, object>
        {
            ["name"] = name.Trim(),
            ["rules"] = BuildRulePayloads(rules)
        };
        string url = $"{_options.ApiBaseUrl}/pronunciation-dictionaries/add-from-rules";

        var result = await PostDictionaryMutationAsync(url, payload, "create dictionary", ct);
        return result.IsFailure
            ? Result<PronunciationLocator>.Failure(result.Error)
            : Result<PronunciationLocator>.Success(new PronunciationLocator(result.Value!.Id, result.Value.VersionId));
    }

    /// <summary>Adds/replaces rules on an existing dictionary; returns the new version id.</summary>
    public async Task<Result<string>> AddRulesAsync(
        string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<string>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(dictionaryId))
            return Result<string>.Failure("A dictionary id is required to add rules.");
        if (rules is null || rules.Count == 0)
            return Result<string>.Failure("At least one rule is required.");

        var payload = new Dictionary<string, object> { ["rules"] = BuildRulePayloads(rules) };
        string url = $"{_options.ApiBaseUrl}/pronunciation-dictionaries/{dictionaryId}/add-rules";

        var result = await PostDictionaryMutationAsync(url, payload, "add dictionary rules", ct);
        return result.IsFailure ? Result<string>.Failure(result.Error) : Result<string>.Success(result.Value!.VersionId);
    }

    /// <summary>Removes rules (by the words they replace); returns the new version id.</summary>
    public async Task<Result<string>> RemoveRulesAsync(
        string dictionaryId, IReadOnlyList<string> ruleStrings, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<string>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(dictionaryId))
            return Result<string>.Failure("A dictionary id is required to remove rules.");
        if (ruleStrings is null || ruleStrings.Count == 0)
            return Result<string>.Failure("At least one rule string is required.");

        var payload = new Dictionary<string, object> { ["rule_strings"] = ruleStrings };
        string url = $"{_options.ApiBaseUrl}/pronunciation-dictionaries/{dictionaryId}/remove-rules";

        var result = await PostDictionaryMutationAsync(url, payload, "remove dictionary rules", ct);
        return result.IsFailure ? Result<string>.Failure(result.Error) : Result<string>.Success(result.Value!.VersionId);
    }

    /// <summary>
    ///     Replaces ALL rules on an existing dictionary with the supplied set, returning the new
    ///     version id. Used to maintain the alias-only version line a phoneme-carrying dictionary
    ///     needs on models that would delete phoneme-matched words.
    /// </summary>
    public async Task<Result<string>> SetRulesAsync(
        string dictionaryId, IReadOnlyList<PronunciationRule> rules, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result<string>.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(dictionaryId))
            return Result<string>.Failure("A dictionary id is required to set rules.");
        if (rules is null || rules.Count == 0)
            return Result<string>.Failure("At least one rule is required.");

        var payload = new Dictionary<string, object> { ["rules"] = BuildRulePayloads(rules) };
        string url = $"{_options.ApiBaseUrl}/pronunciation-dictionaries/{dictionaryId}/set-rules";

        var result = await PostDictionaryMutationAsync(url, payload, "set dictionary rules", ct);
        return result.IsFailure ? Result<string>.Failure(result.Error) : Result<string>.Success(result.Value!.VersionId);
    }

    /// <summary>
    ///     Archives a pronunciation dictionary (PATCH <c>archived=true</c>). ElevenLabs has no
    ///     hard delete for dictionaries — archiving is the closest removal: it takes the
    ///     dictionary out of active use while staying recoverable. Used to roll back an upstream
    ///     create whose local record could not be persisted (an otherwise permanent orphan).
    ///     Idempotent: a 404 (dictionary already gone) is treated as success.
    /// </summary>
    public async Task<Result> ArchiveAsync(string dictionaryId, CancellationToken ct = default)
    {
        if (!HasApiKey)
            return Result.Failure(ErrorMessages.Configuration.AudioApiKeyNotConfigured);
        if (string.IsNullOrWhiteSpace(dictionaryId))
            return Result.Failure("A dictionary id is required to archive.");

        var payload = new Dictionary<string, object> { ["archived"] = true };
        string url = $"{_options.ApiBaseUrl}/pronunciation-dictionaries/{dictionaryId}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(payload) };
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Success(); // already gone — nothing left to archive

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("ElevenLabs archive dictionary failed. StatusCode: {StatusCode}, Response: {Error}",
                    response.StatusCode, error);
                return Result.Failure(FormatElevenLabsError(response.StatusCode, error));
            }

            logger.LogInformation("ElevenLabs archive dictionary ok — DictionaryId={DictionaryId}", dictionaryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error archiving ElevenLabs pronunciation dictionary {DictionaryId}", dictionaryId);
            return Result.Failure($"Error archiving dictionary: {ex.Message}");
        }
    }

    private async Task<Result<PronunciationDictionaryMutationResponse>> PostDictionaryMutationAsync(
        string url, object payload, string operation, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            request.Headers.Add("xi-api-key", _options.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("ElevenLabs {Operation} failed. StatusCode: {StatusCode}, Response: {Error}",
                    operation, response.StatusCode, error);
                return Result<PronunciationDictionaryMutationResponse>.Failure(
                    FormatElevenLabsError(response.StatusCode, error));
            }

            var data = await response.Content.ReadFromJsonAsync<PronunciationDictionaryMutationResponse>(ct);
            if (data is null || string.IsNullOrWhiteSpace(data.VersionId))
                return Result<PronunciationDictionaryMutationResponse>.Failure(
                    $"ElevenLabs {operation} response did not include a version id.");

            logger.LogInformation("ElevenLabs {Operation} ok — DictionaryId={DictionaryId}, VersionId={VersionId}",
                operation, data.Id, data.VersionId);
            return Result<PronunciationDictionaryMutationResponse>.Success(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during ElevenLabs {Operation}", operation);
            return Result<PronunciationDictionaryMutationResponse>.Failure($"Error during {operation}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Builds the wire rules, ORDERED LONGEST <c>string_to_replace</c> FIRST.
    ///     <para>
    ///         ElevenLabs applies alias rules in the order supplied and the FIRST match wins, so a
    ///         general rule listed before a more specific one silently swallows it. Measured against
    ///         the live API on 2026-08-24 with a two-rule probe dictionary and identical text
    ///         ("The Warringah Freeway is busy this morning."):
    ///     </para>
    ///     <list type="bullet">
    ///         <item>general first ("Warringah" -> banana, then "Warringah Freeway" -> kiwi): heard "The Banana Freeway"</item>
    ///         <item>specific first (same two rules, reversed): heard "The kiwi"</item>
    ///     </list>
    ///     <para>
    ///         That is the defect ARN reported on test job <c>a907ac7b</c>: the Sydney Roads
    ///         dictionary carried "Warringah" -> "Were-rin-gah" at position 8 and an operator's later
    ///         "Warringah Freeway" fix at position 19, so the fix never fired and two dictionary
    ///         edits appeared to do nothing. Sorting here rather than at the callers because every
    ///         push sends the COMPLETE desired rule set (see PronunciationDictionaryService), and a
    ///         re-push in a new order provably reorders the stored lexicon - so one sort at the wire
    ///         boundary corrects every existing dictionary on its next version.
    ///     </para>
    ///     Ties break on the replaced string so the payload stays deterministic and a version is not
    ///     re-created for an ordering that did not really change.
    /// </summary>
    private static List<Dictionary<string, object>> BuildRulePayloads(IReadOnlyList<PronunciationRule>? rules)
    {
        var payloads = new List<Dictionary<string, object>>();
        if (rules is null) return payloads;
        foreach (var r in rules
                     .OrderByDescending(x => x.StringToReplace?.Length ?? 0)
                     .ThenBy(x => x.StringToReplace, StringComparer.Ordinal))
        {
            var rule = new Dictionary<string, object>
            {
                ["string_to_replace"] = r.StringToReplace,
                ["case_sensitive"] = r.CaseSensitive,
                ["word_boundaries"] = r.WordBoundaries
            };
            if (r.Type == PronunciationRuleType.Phoneme)
            {
                rule["type"] = "phoneme";
                rule["phoneme"] = r.Replacement;
                rule["alphabet"] = r.Alphabet ?? "ipa";
            }
            else
            {
                rule["type"] = "alias";
                rule["alias"] = r.Replacement;
            }
            payloads.Add(rule);
        }
        return payloads;
    }

    /// <summary>
    ///     Surfaces ElevenLabs' human-readable error (<c>detail.message</c>) instead of the raw
    ///     JSON body — e.g. "Only your own custom voices can be remixed." Falls back to the status.
    /// </summary>
    private static string FormatElevenLabsError(HttpStatusCode status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.Object &&
                    detail.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? body;
                if (detail.ValueKind == JsonValueKind.String)
                    return detail.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Body wasn't JSON — fall through to the status code.
        }

        return $"ElevenLabs request failed ({(int)status} {status}).";
    }

    // =========================================================================
    // Request Building
    // =========================================================================

    private object BuildTtsPayload(
        string text,
        double speed,
        double stability,
        double similarityBoost,
        double style,
        bool useSpeakerBoost,
        string modelId,
        string? market = null,
        PronunciationStack? pronunciationStack = null)
    {
        // v2-family models (multilingual v2, turbo v2/v2.5, flash v2.5) support the classic
        // continuous voice_settings knobs; v3 buckets stability to 3 discrete presets and
        // silently ignores style/speaker-boost/similarity/speed entirely (verified empirically).
        // A single shared check avoids the three flags drifting out of sync with each other or
        // with new v2-family model ids (e.g. eleven_flash_v2_5, previously missed here — it
        // matched neither "multilingual_v2" nor "turbo_v2", so its speed/style/speaker-boost were
        // silently dropped even though ElevenLabs honors them for that model).
        bool isV2Family = modelId.Contains("multilingual_v2", StringComparison.OrdinalIgnoreCase) ||
                           modelId.Contains("turbo_v2", StringComparison.OrdinalIgnoreCase) ||
                           modelId.Contains("flash_v2", StringComparison.OrdinalIgnoreCase);

        bool supportsStyle = isV2Family;
        bool supportsSpeakerBoost = isV2Family;
        bool supportsSimilarity = isV2Family;

        // v3 ignores `speed` entirely (verified empirically); pace on v3 comes
        // from the post-process time-stretch, not the model. Only the v2-family
        // models honor it.
        bool supportsSpeed = isV2Family;

        bool discreteStability = modelId.Contains("v3", StringComparison.OrdinalIgnoreCase);

        // ElevenLabs honors `speed` ONLY inside voice_settings — a top-level
        // `speed` is silently ignored for every model.
        var voiceSettings = new Dictionary<string, object>
        {
            ["stability"] = discreteStability ? ClampToDiscreteStability(stability) : stability,
            ["similarity_boost"] = supportsSimilarity ? similarityBoost : 0.0,
            ["style"] = supportsStyle ? style : 0.0,
            ["use_speaker_boost"] = supportsSpeakerBoost && useSpeakerBoost
        };

        if (supportsSpeed)
            voiceSettings["speed"] = speed;

        var payload = new Dictionary<string, object>
        {
            ["text"] = text,
            ["model_id"] = modelId,
            ["voice_settings"] = voiceSettings,
            ["apply_text_normalization"] = _options.TextSettings.ApplyTextNormalization ? "on" : "auto"
        };

        if (!string.IsNullOrEmpty(_options.LanguageCode))
            payload["language_code"] = _options.LanguageCode;

        if (_options.Seed.HasValue)
            payload["seed"] = _options.Seed.Value;

        // The resolved stack is the ONLY source of locators. Order IS precedence: ElevenLabs
        // applies locators sequentially and the first one to match a word wins it, while
        // non-colliding rules from every layer still apply (verified live 2026-07-31). The stack
        // was built station-first for exactly that reason, so it is emitted verbatim.
        if (pronunciationStack is { IsEmpty: false })
        {
            var attached = pronunciationStack.Locators;
            if (attached.Count > PronunciationStack.MaxLocators)
            {
                // Unreachable via PronunciationStack.Of, which builds at most two — a defensive
                // assert so a future third layer cannot silently exceed the provider's cap.
                logger.LogError(
                    "Pronunciation stack carried {Count} locators but ElevenLabs accepts {Max}; the excess was dropped.",
                    attached.Count, PronunciationStack.MaxLocators);
                attached = [.. attached.Take(PronunciationStack.MaxLocators)];
            }

            payload["pronunciation_dictionary_locators"] = attached
                .Select(l => new PronunciationDictionaryLocator(l.DictionaryId, l.VersionId))
                .ToArray();

            LogAttachedLayer(pronunciationStack.Station, "station", market, modelId);
            LogAttachedLayer(pronunciationStack.Global, "platform-wide", market, modelId);
        }
        return payload;
    }

    /// <summary>
    ///     Records which dictionary each layer contributed, and warns when a model will skip its
    ///     phoneme rules. Logged per layer so a bulletin's trace names both dictionaries by role.
    /// </summary>
    private void LogAttachedLayer(
        Option<PronunciationLocator> layer, string role, string? market, string modelId)
    {
        if (!layer.HasValue)
            return;

        var locator = layer.Value;
        logger.LogInformation(
            "Pronunciation dictionary attached — Market: {Market}, Layer: {Layer}, DictionaryId={DictionaryId}, VersionId={VersionId}",
            market ?? "(default)", role, locator.DictionaryId, locator.VersionId);

        if (modelId.Contains("v3", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "eleven_v3 applies ONLY alias dictionary rules — phoneme/IPA rules are skipped. " +
                "Layer={Layer}, DictionaryId={DictionaryId}, ModelId={ModelId}",
                role, locator.DictionaryId, modelId);
        }
    }

    // =========================================================================
    // Response Parsing
    // =========================================================================

    private async Task<Result<ElevenLabsAudioResult>> ParseAudioResponse(HttpResponseMessage response,
        CancellationToken ct)
    {
        byte[] audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
        (byte[] outputBytes, double duration) = FinalizeAudio(audioBytes);
        return new ElevenLabsAudioResult(outputBytes, duration);
    }

    /// <summary>
    ///     Parses response from the /with-timestamps endpoint.
    ///     Extracts precise duration from character timing alignment data.
    /// </summary>
    private async Task<Result<ElevenLabsAudioResult>> ParseTimestampsResponse(HttpResponseMessage response,
        CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<TimestampsResponse>(ct);
        if (json is null || string.IsNullOrEmpty(json.AudioBase64))
            return Result<ElevenLabsAudioResult>.Failure("Invalid timestamps response: missing audio data");

        byte[] audioBytes = Convert.FromBase64String(json.AudioBase64);
        (byte[] wavBytes, double fallbackDuration) = FinalizeAudio(audioBytes);

        var alignment = json.NormalizedAlignment ?? json.Alignment;
        double duration = alignment?.CharacterEndTimesSeconds.Count > 0
            ? alignment.CharacterEndTimesSeconds[^1]
            : fallbackDuration;

        // Word-timing alignment must match the INPUT text (the script the operator sees),
        // so prefer the raw alignment over the normalized one ("26" -> "twenty six").
        var inputAlignment = json.Alignment ?? json.NormalizedAlignment;
        AudioAlignment? domainAlignment = inputAlignment is { Characters.Count: > 0 }
            ? new AudioAlignment(
                inputAlignment.Characters,
                inputAlignment.CharacterStartTimesSeconds,
                inputAlignment.CharacterEndTimesSeconds)
            : null;

        return new ElevenLabsAudioResult(wavBytes, duration, domainAlignment);
    }

    /// <summary>
    ///     Converts raw audio bytes to final output format (WAV-wrapped PCM or passthrough MP3)
    ///     and estimates duration from byte length.
    /// </summary>
    private (byte[] OutputBytes, double EstimatedDuration) FinalizeAudio(byte[] audioBytes)
    {
        int sampleRate = ParseSampleRate(_options.OutputFormat);

        if (IsPcmFormat)
        {
            double duration = CalculatePcmDuration(audioBytes.Length, sampleRate);
            byte[] wavBytes = WavHeaderBuilder.WrapInWavHeader(audioBytes, sampleRate, MonoChannels, BitsPerSample16);
            return (wavBytes, duration);
        }

        double estimatedDuration = CalculateMp3Duration(audioBytes.Length);
        logger.LogDebug(
            "MP3 duration estimated at {Duration:F3}s using CBR assumption ({Bitrate}bps). Actual VBR duration may differ.",
            estimatedDuration, DefaultMp3Bitrate);
        return (audioBytes, estimatedDuration);
    }

    private static double CalculatePcmDuration(int byteCount, int sampleRate)
    {
        return byteCount / (sampleRate * (double)BytesPerSample16Bit);
    }

    /// <summary>
    ///     Estimates MP3 duration assuming constant bitrate (CBR) at <see cref="DefaultMp3Bitrate" />.
    ///     ElevenLabs may produce VBR output, making this an approximation.
    ///     The timestamps endpoint provides precise alignment-based duration; this is a fallback only.
    /// </summary>
    private static double CalculateMp3Duration(int byteCount)
    {
        return byteCount * BitsPerByte / (double)DefaultMp3Bitrate;
    }

    private static int ParseSampleRate(string outputFormat)
    {
        string[] parts = outputFormat.Split('_');
        return parts.Length >= 2 && int.TryParse(parts[1], out int rate) ? rate : DefaultSampleRate;
    }

    // =========================================================================
    // Utilities
    // =========================================================================

    private double ClampSpeed(double speed)
    {
        return Math.Clamp(speed, _options.MinSpeed, _options.MaxSpeed);
    }

    private static double ClampToDiscreteStability(double value)
    {
        return value switch
        {
            <= 0.25 => 0.0,
            <= 0.75 => 0.5,
            _ => 1.0
        };
    }
}
