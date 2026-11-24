using System.Globalization;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;
/// <summary>

///     Auditions a draft read WITHOUT touching the job.
///
///     Delivery on eleven_v3 is only steerable by trying it: which audio tags a voice acts on is
///     voice-dependent and undocumented, and ellipses, capitalisation and sentence fragments move
///     the read as much as the tags do (verified by listening, 2026-08-03). An operator therefore
///     needs a fast loop. Re-voice is not one: it costs a full orchestration, re-runs safety,
///     refits the slot and pushes the job to WAITING_APPROVAL, so a second attempt needs a
///     rejection first.
///
///     This synthesises the supplied text VERBATIM — tags included, so they can be heard — on the
///     job's own voice, settings, model and market, and returns a playable URL. It never persists
///     to the job, never changes status, and its audio never reaches air, so no safety review runs
///     here: the words are reviewed when they are actually submitted through regenerate.
///
///     The optional <c>VoiceProfileId</c> answers the other half of the pre-commit question —
///     "how would a DIFFERENT voice read this?" — by substituting the profile's current
///     voice/model/settings for the job's (market unchanged). The returned <see cref="AuditionScriptResponseDto.DurationSec" />
///     then doubles as the slot-fit probe for a voice change: same text, candidate voice, real
///     duration.
/// </summary>
public sealed class ScriptAuditionHttpTrigger(
    IJobRepository jobRepository,
    IMarketAccessService marketAccess,
    IVoiceProfileService voiceProfileService,
    IAudioSynthesizer audioSynthesizer,
    IBroadcastOutput broadcastOutput,
    IAudioUrlProvider audioUrlProvider,
    IAudioProcessor audioProcessor,
    TimeProvider timeProvider,
    ILogger<ScriptAuditionHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    /// <summary>A bulletin is ~100 words; this is generous headroom, not a content rule.</summary>
    private const int MaxAuditionChars = 3000;

    [Function("AuditionScript")]
    public async Task<HttpResponseData> Audition(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/audition")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        var cancellationToken = ctx.CancellationToken;
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, cancellationToken) is { } forbidden)
            return forbidden;

        var parsed = await HttpRequestParser.ParseAsync<AuditionScriptRequestDto>(req, cancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        string text = parsed.Value.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return await HttpResponseHelper.BadRequestAsync(req, "Text is required to audition a read.");
        if (text.Length > MaxAuditionChars)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Text must be at most {MaxAuditionChars} characters to audition.");

        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, $"Job '{jobId}' not found.");
        var job = jobResult.Value;

        // The audition is worthless if it does not use the read the bulletin would actually get,
        // so voice, settings, model and market all come from the job rather than from defaults.
        var voice = job.VoiceConfig;
        var voiceId = voice.Match(v => Option<string>.Some(v.VoiceId), () => Option<string>.None);
        var model = voice.Match(v => Option<string>.Some(v.Model), () => Option<string>.None);
        var settings = voice.Match(v => v.Settings, () => Option<VoiceSettings>.None);

        // An operator trialling a DIFFERENT voice (pre-commit listen before regenerate) names a
        // voice profile: it is resolved server-side to the profile's CURRENT tuning — the same
        // trust rule as the regenerate override, never a client-supplied snapshot — and its
        // voice/model/settings replace the job's. Market stays the job's: slot-fit depends on
        // the read length, which depends on the voice, not the market label. Unresolvable is a
        // hard 400 naming the profile (mirrors the regenerate path).
        if (!string.IsNullOrWhiteSpace(parsed.Value.VoiceProfileId))
        {
            var profileResult = await voiceProfileService.GetAsync(parsed.Value.VoiceProfileId, cancellationToken);
            if (profileResult.IsFailure || profileResult.Value.Versions.Count == 0)
            {
                string reason = (profileResult.IsFailure ? profileResult.Error : "it has no versions").TrimEnd('.');
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Voice profile '{parsed.Value.VoiceProfileId}' could not be resolved: {reason}.");
            }

            var profile = profileResult.Value;
            var current = profile.Versions[^1];
            voiceId = Option<string>.Some(profile.BaseVoiceId);
            model = Option<string>.Some(current.Model);
            settings = Option<VoiceSettings>.Some(current.Settings);
        }

        var pronunciationStack = BuildPronunciationStack(job);
        string dictionaryKey = DictionaryKey(pronunciationStack);
        string styleKey = StyleKey(job);
        string revision = job.ETag
            ?? job.LastUpdatedAt.Ticks.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(parsed.Value.VoiceProfileId))
            revision += $"|profile={parsed.Value.VoiceProfileId}";

        string selectedVoiceId = voiceId.GetValueOr(string.Empty);
        string selectedModel = model.GetValueOr(string.Empty);
        string settingsKey = settings.HasValue
            ? settings.Value.GetConfigurationKey(selectedVoiceId)
            : "default";
        var key = new InteractiveWorkKey(
            PrincipalKey(req),
            "audition",
            revision,
            $"{text}\u001fmarket={job.Market}",
            selectedModel,
            settingsKey,
            dictionaryKey,
            styleKey);

        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            key,
            async operationCt =>
            {
                var synthesis = await audioSynthesizer.SynthesizeAsync(
                    text, settings, voiceId, model, operationCt,
                    Option<string>.Some(job.Market), pronunciationStack);
                if (synthesis.IsFailure)
                    return synthesis.PropagateFailure<AuditionScriptResponseDto>();

                byte[] audio = synthesis.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                string fileName =
                    $"audition_{jobId}_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var saved = await broadcastOutput.SaveAsync(audio, fileName, operationCt);
                if (saved.IsFailure)
                    return saved.PropagateFailure<AuditionScriptResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var url = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                double duration = audioProcessor.GetDuration(audio);
                // What the operator HEARD, not what they typed: the synthesizer strips tags for a
                // voice that cannot interpret them, so on a non-v3 model this is 0 and the UI
                // must not claim otherwise.
                int tagCount = DeliveryTags.Extract(
                    DeliveryTags.ForModel(text, selectedModel)).Count;

                logger.LogInformation(
                    "[Audition] Job {JobId} auditioned {Chars} chars with {Tags} tag(s) -> {Duration:F1}s",
                    jobId, text.Length, tagCount, duration);

                return Result<AuditionScriptResponseDto>.Success(
                    new AuditionScriptResponseDto(
                        url.IsSuccess ? url.Value : string.Empty,
                        Math.Round(duration, 1),
                        tagCount));
            },
            cancellationToken);

        if (operation.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, operation.Error);

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

    private static PronunciationStack BuildPronunciationStack(Job job)
    {
        var station = job.PronunciationDictionaryId.HasValue && job.PronunciationVersionId.HasValue
            ? Option<PronunciationLocator>.Some(new PronunciationLocator(
                job.PronunciationDictionaryId.Value, job.PronunciationVersionId.Value))
            : Option<PronunciationLocator>.None;
        var global = job.GlobalPronunciationDictionaryId.HasValue && job.GlobalPronunciationVersionId.HasValue
            ? Option<PronunciationLocator>.Some(new PronunciationLocator(
                job.GlobalPronunciationDictionaryId.Value, job.GlobalPronunciationVersionId.Value))
            : Option<PronunciationLocator>.None;
        return PronunciationStack.Of(station, global);
    }

    private static string DictionaryKey(PronunciationStack stack) =>
        string.Join("|", stack.Locators.Select(locator => $"{locator.DictionaryId}:{locator.VersionId}"));

    private static string StyleKey(Job job) =>
        job.DeliveryStyleId.HasValue
            ? $"{job.DeliveryStyleId.Value}:v{job.DeliveryStyleVersion.GetValueOr(0)}"
            : string.Empty;
}

public sealed record AuditionScriptRequestDto(
    string? Text = null,
    /// <summary>
    ///     Optional voice profile to audition with INSTEAD of the job's voice. Resolved
    ///     server-side to the profile's CURRENT version (same trust rule as the regenerate
    ///     override — never a client-supplied snapshot); unresolvable → 400 naming the profile.
    /// </summary>
    string? VoiceProfileId = null);

public sealed record AuditionScriptResponseDto(string Url, double DurationSec, int TagCount);
