using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Renders ARBITRARY copy — an advertisement, a promo, a station ident — on a library voice,
///     and hands back a file the operator can download and hand to production.
///
///     Every other synthesis endpoint answers a question about the platform: preview asks "how do
///     these settings sound", audition asks "how would THIS bulletin read". This one produces a
///     deliverable that never touches a bulletin, so it is deliberately not job- or market-scoped:
///     an ad read belongs to the voice library, not to a station's traffic slot.
///
///     Two routes, because the download has to actually download. The POST synthesises and returns
///     a RELATIVE url; the GET streams those bytes back through our own origin with an attachment
///     disposition. A cross-origin SAS url cannot do that — the browser ignores an anchor's
///     `download` attribute on another origin, so the operator would land on a media viewer instead
///     of a saved file.
///
///     What "current configuration" means here: the profile's newest version — its base voice,
///     model and voice settings (speed, stability, similarity, style, speaker boost), resolved
///     server-side from the profile id, never from a client-supplied snapshot (the audition trust
///     rule). Two things are deliberately NOT applied. The version's pace boost is slot-fitting
///     time-compression for bulletins, and an advertisement has no slot to fit; a station
///     pronunciation dictionary is per-station, and a voiceover has no station.
/// </summary>
public sealed class VoiceoverHttpTrigger(
    IVoiceProfileService voiceProfileService,
    IAudioSynthesizer audioSynthesizer,
    IAudioProcessor audioProcessor,
    IBroadcastOutput broadcastOutput,
    IAudioUrlProvider audioUrlProvider,
    TimeProvider timeProvider,
    ILogger<VoiceoverHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    /// <summary>
    ///     Only files this endpoint itself wrote are servable. Without the pattern the GET would
    ///     stream ANY blob in the broadcasts container by name — including bulletin audio, whose
    ///     market guard lives on the job routes, not here.
    /// </summary>
    private static readonly Regex VoiceoverFileName = new(
        @"^voiceover_[a-z0-9-]{1,64}_\d{8}_\d{6}_\d{3}\.wav$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Declared as a literal sub-route of voices/profiles/{id} so it cannot be mistaken for a
    // profile id segment.
    [Function("SynthesizeVoiceover")]
    public async Task<HttpResponseData> Synthesize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/{id}/voiceover")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");
        if (ArnBulletinGenerator.Functions.Http.StorageKey.RejectIfInvalid(req, "id", id) is { } badKey)
            return await badKey;

        var parsed = await HttpRequestParser.ParseAsync<VoiceoverRequestDto>(req, ct);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        string script = parsed.Value.Script?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            return await HttpResponseHelper.BadRequestAsync(req, "A script is required to record a voiceover.");
        // The same ceiling the direct /synthesize route uses: one request must not be able to bill
        // an arbitrarily long read.
        if (script.Length > ConfigValidationLimits.MaxSynthesisScriptLength)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"A voiceover script must be at most {ConfigValidationLimits.MaxSynthesisScriptLength} characters "
                + $"(received {script.Length}).");

        var profileResult = await voiceProfileService.GetAsync(id, ct);
        if (profileResult.IsFailure || profileResult.Value.Versions.Count == 0)
        {
            string reason = (profileResult.IsFailure ? profileResult.Error : "it has no versions").TrimEnd('.');
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Voice profile '{id}' could not be resolved: {reason}.");
        }

        var profile = profileResult.Value;
        var current = profile.Versions[^1];

        string settingsKey = current.Settings.GetConfigurationKey(profile.BaseVoiceId);
        var key = new InteractiveWorkKey(
            PrincipalKey(req),
            "voiceover",
            $"{profile.Id}:v{current.Version.ToString(CultureInfo.InvariantCulture)}",
            script,
            current.Model,
            settingsKey,
            string.Empty,
            string.Empty);

        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            key,
            async operationCt =>
            {
                var synthesis = await audioSynthesizer.SynthesizeAsync(
                    script,
                    Option<VoiceSettings>.Some(current.Settings),
                    Option<string>.Some(profile.BaseVoiceId),
                    Option<string>.Some(current.Model),
                    operationCt,
                    Option<string>.None);
                if (synthesis.IsFailure)
                    return synthesis.PropagateFailure<VoiceoverResponseDto>();

                byte[] audio = synthesis.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                string fileName =
                    $"voiceover_{Sanitize(profile.Id)}_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}.wav";
                var saved = await broadcastOutput.SaveAsync(
                    audio, Path.GetFileNameWithoutExtension(fileName), operationCt);
                if (saved.IsFailure)
                    return saved.PropagateFailure<VoiceoverResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                double duration = audioProcessor.GetDuration(audio);
                int wordCount = script.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

                logger.LogInformation(
                    "[Voiceover] Profile {ProfileId} rendered {Words} word(s) on {VoiceId}/{Model} -> {Duration:F1}s",
                    profile.Id, wordCount, profile.BaseVoiceId, current.Model, duration);

                return Result<VoiceoverResponseDto>.Success(
                    new VoiceoverResponseDto(
                        $"voices/voiceovers/{fileName}", fileName, Math.Round(duration, 1), wordCount));
            },
            ct);

        if (operation.IsFailure)
            // An unusable voice or setting combination is operator-correctable input, and the
            // synthesizer already returns a typed InvalidInput with a sanitised provider message.
            return await HttpResponseHelper.MapTypedFailureAsync(req, operation.Error,
                operation.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        if (previewAudioCleanup is not null)
            await previewAudioCleanup.PinAsync(operation.Value.FileName, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

    /// <summary>
    ///     Streams a rendered voiceover back through our own origin as an attachment, so a plain
    ///     anchor saves it under a sensible name. Range-aware for the same reason job audio is:
    ///     the console's player seeks, and Chrome will not seek a response it cannot range-request.
    /// </summary>
    [Function("GetVoiceover")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "voices/voiceovers/{fileName}")]
        HttpRequestData req,
        string fileName, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !VoiceoverFileName.IsMatch(fileName))
            return await HttpResponseHelper.BadRequestAsync(req, "Not a voiceover file name.");

        var range = JobsHttpTrigger.ParseRangeHeader(
            req.Headers.TryGetValues("Range", out var rangeValues) ? rangeValues.FirstOrDefault() : null);

        var streamResult = await audioUrlProvider.OpenAudioStreamAsync(fileName, range, ctx.CancellationToken);
        if (streamResult.IsFailure)
        {
            if (streamResult.Error.Contains("not satisfiable", StringComparison.OrdinalIgnoreCase))
                return req.CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable);
            return await HttpResponseHelper.NotFoundAsync(req, streamResult.Error);
        }

        await using var handle = streamResult.Value;
        var response = req.CreateResponse(handle.IsPartial ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
        response.Headers.Add("Content-Type", handle.ContentType);
        response.Headers.Add("Accept-Ranges", "bytes");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        if (handle.IsPartial)
            response.Headers.Add(
                "Content-Range",
                $"bytes {handle.RangeStart}-{handle.RangeStart + handle.ContentLength - 1}/{handle.TotalLength}");
        await handle.Content.CopyToAsync(response.Body, ctx.CancellationToken);
        return response;
    }

    /// <summary>Keeps the blob name inside the pattern the GET will accept.</summary>
    private static string Sanitize(string profileId) =>
        new string(profileId.Where(char.IsAsciiLetterOrDigit).Take(64).ToArray()) is { Length: > 0 } clean
            ? clean
            : "voice";
}

public sealed record VoiceoverRequestDto(string? Script = null);

public sealed record VoiceoverResponseDto(string Url, string FileName, double DurationSec, int WordCount);
