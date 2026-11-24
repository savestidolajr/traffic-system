using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Per-market verified-pronunciation management: the words an operator has listened to and
///     confirmed air correctly, so the audio pre-review stops flagging them on every bulletin.
///     Operators own this list (they are the ones doing the listening), so the write route is
///     Operator + market-scoped, mirroring priority roads rather than the Admin-only safety
///     terms. Whole-list PUT — the caller sends the full set, exactly as priority roads and
///     filler statements do.
/// </summary>
public sealed class VerifiedPronunciationsHttpTrigger(
    IVerifiedPronunciationRepository repository,
    IScheduleRepository scheduleRepository,
    IMarketAccessService marketAccess,
    TimeProvider timeProvider,
    ILogger<VerifiedPronunciationsHttpTrigger> logger)
{
    [Function("GetVerifiedPronunciations")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/verified-pronunciations")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await repository.GetForMarketAsync(market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, new VerifiedPronunciationsDto(market, ToDtos(result.Value)));
    }

    [Function("SetVerifiedPronunciations")]
    public async Task<HttpResponseData> Set(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "markets/{market}/verified-pronunciations")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");
        if (StorageKey.RejectIfInvalid(req, "market", market) is { } badKey)
            return await badKey;

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var scheduleResult = await scheduleRepository.GetScheduleAsync(market, ctx.CancellationToken);
        if (scheduleResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, scheduleResult.Error);

        var parsed = await HttpRequestParser.ParseAsync<UpsertVerifiedPronunciationsRequest>(
            req, ctx.CancellationToken);
        if (parsed.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        if (parsed.Value.Words is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Body must contain a 'words' array.");

        // Validate the payload the client actually sent, THEN build. The previous shape filtered
        // blanks and silently de-duplicated by normalised key before checking, so a caller whose
        // word collided with an existing one (or whose payload carried blanks) got 200 with their
        // entry, note and verifier silently never existing — while the config-import path rejects
        // the identical payload with 400. Two write paths to the same data must enforce the same
        // rule (mirrors the priority-roads route's count-first shape).
        var incoming = parsed.Value.Words;

        if (incoming.Count > ConfigValidationLimits.MaxVerifiedPronunciations)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"At most {ConfigValidationLimits.MaxVerifiedPronunciations} verified pronunciations per market (received {incoming.Count}).");

        if (incoming.Any(w => w is null || string.IsNullOrWhiteSpace(w.Word)))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Every verified pronunciation needs a word.");

        if (incoming.Any(w => w!.Word.Trim().Length > ConfigValidationLimits.MaxVerifiedPronunciationLength))
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Words must be at most {ConfigValidationLimits.MaxVerifiedPronunciationLength} characters.");

        if (incoming.Any(w => w!.Note is { Length: > ConfigValidationLimits.MaxVerifiedPronunciationNoteLength }))
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Notes must be at most {ConfigValidationLimits.MaxVerifiedPronunciationNoteLength} characters.");

        // Punctuation/case-insensitive identity, matching the web client's chip filter —
        // "Gate's" and "Gates" are one verdict, not two — and matching the import planner's
        // rejection of the same collision.
        var duplicates = incoming
            .GroupBy(w => VerifiedPronunciation.NormalizeWord(w!.Word.Trim()), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Each word may appear once. Duplicated: {string.Join(", ", duplicates)}.");

        string updatedBy = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Existing entries keep their original verifier/timestamp: a PUT that only toggles or
        // removes one word must not rewrite the provenance of every other word in the list.
        var existing = await repository.GetForMarketAsync(market, ctx.CancellationToken);
        var priorByKey = (existing.IsSuccess ? existing.Value : [])
            .GroupBy(w => w.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var words = incoming
            .Select(w =>
            {
                string trimmed = w.Word.Trim();
                var prior = priorByKey.GetValueOrDefault(VerifiedPronunciation.NormalizeWord(trimmed));
                return new VerifiedPronunciation(
                    trimmed,
                    string.IsNullOrWhiteSpace(w.Note) ? null : w.Note.Trim(),
                    w.Enabled,
                    prior?.VerifiedBy ?? updatedBy,
                    prior?.VerifiedAtUtc is { } at && at != default ? at : now);
            })
            .ToList();

        var result = await repository.SetForMarketAsync(market, words, updatedBy, ctx.CancellationToken);
        if (result.IsFailure)
            // Typed mapping: the repository's over-ceiling pre-flight is a typed InvalidInput that
            // must map to 400 — message matching alone cannot see it and would send a permanent
            // client-input error to a 500. Untyped failures (ETag "Conflict: ...") still fall
            // through to the 409/500 message mapping below.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) =>
                    e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                    e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                        ? HttpResponseHelper.BadRequestAsync(r, e)
                        : HttpResponseHelper.InternalErrorAsync(r, e));

        logger.LogInformation("[VerifiedPronunciations] {Market} updated by {User}: {Count} word(s)",
            market, updatedBy, words.Count);

        return await HttpResponseHelper.OkJsonAsync(req, new VerifiedPronunciationsDto(market, ToDtos(words)));
    }

    /// <summary>
    ///     Aggregate inventory: verified pronunciations for every requested market the caller can
    ///     access, in ONE round-trip. Mirrors <c>priority-roads/inventory</c> — the review cockpit
    ///     and the management tab both want the whole picture without a per-market fan-out.
    /// </summary>
    [Function("VerifiedPronunciationsInventory")]
    public async Task<HttpResponseData> Inventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "verified-pronunciations/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var principal = ClientPrincipal.Parse(req);
        var allowed = principal is null
            ? Option<IReadOnlyList<string>>.None
            : await marketAccess.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
        var markets = MarketScopeRequest.FilterToAllowed(
            MarketScopeRequest.ParseMarkets(MarketScopeRequest.Query(req, "markets")), allowed);

        var results = await Task.WhenAll(
            markets.Select(m => repository.GetForMarketAsync(m, ctx.CancellationToken)));

        var inventory = new List<VerifiedPronunciationsDto>(markets.Count);
        for (int i = 0; i < markets.Count; i++)
            inventory.Add(new VerifiedPronunciationsDto(
                markets[i], results[i].IsSuccess ? ToDtos(results[i].Value) : []));

        return await HttpResponseHelper.OkJsonAsync(req, inventory);
    }

    internal static VerifiedPronunciationDto ToDto(VerifiedPronunciation w) => new(
        w.Word, w.Note, w.Enabled, w.VerifiedBy,
        w.VerifiedAtUtc == default ? null : w.VerifiedAtUtc.ToString("O"));

    internal static IReadOnlyList<VerifiedPronunciationDto> ToDtos(IReadOnlyList<VerifiedPronunciation> words) =>
        words.Select(ToDto).ToList();
}
