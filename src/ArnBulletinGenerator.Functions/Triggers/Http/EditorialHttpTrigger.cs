using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Numeric editorial settings endpoints. Stored values layer Global → Market → Station and
///     remain separate from the station guidance system.
/// </summary>
public sealed class EditorialHttpTrigger(
    IEditorialSettingsRepository settingsRepository,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    TimeProvider timeProvider,
    ILogger<EditorialHttpTrigger> logger)
{
    /// <summary>
    ///     Reads and validates the scope pair. Requiring the scope explicitly (rather than defaulting
    ///     to Global) is deliberate: a missing parameter should not silently edit network-wide policy.
    /// </summary>
    private static bool TryReadScope(
        HttpRequestData req, out EditorialScope scope, out string scopeId, out string? error)
    {
        scope = EditorialScope.Global;
        scopeId = string.Empty;
        error = null;

        string? raw = Query(req, "scope");
        if (string.IsNullOrWhiteSpace(raw) || !Enum.TryParse(raw, ignoreCase: true, out scope))
        {
            error = "Query 'scope' must be Global, Market, or Station.";
            return false;
        }

        if (scope == EditorialScope.Global)
            return true;

        scopeId = EditorialSettings.NormalizeScopeId(scope, Query(req, "scopeId"));
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            error = "Query 'scopeId' is required for Market/Station scope.";
            return false;
        }

        return true;
    }

    private static string? Query(HttpRequestData req, string name) =>
        System.Web.HttpUtility.ParseQueryString(req.Url.Query)[name];


    // ── Numeric settings ─────────────────────────────────────────────

    /// <summary>
    ///     The numeric editorial settings stored at one scope. Every field may be null, meaning
    ///     "inherit, ultimately the shipped default" — the console shows the effective value beside it
    ///     via <see cref="GetEffectiveEditorialSettings" />.
    /// </summary>
    [Function("GetEditorialSettings")]
    public async Task<HttpResponseData> GetEditorialSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "editorial/settings")]
        HttpRequestData req, FunctionContext ctx)
    {
        if (!TryReadScope(req, out var scope, out string scopeId, out string? scopeError))
            return await HttpResponseHelper.BadRequestAsync(req, scopeError!);

        var result = await settingsRepository.GetAsync(scope, scopeId, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, EditorialSettingsDto.From(
            result.Value ?? new EditorialSettings(scope, scopeId)));
    }

    /// <summary>
    ///     What a bulletin for this market/station would actually use, after Global → Market → Station
    ///     overlay, with the shipped default filled in for anything still unset. This is the answer to
    ///     "so what IS the corridor cap here?", which no single stored row can give.
    /// </summary>
    [Function("GetEffectiveEditorialSettings")]
    public async Task<HttpResponseData> GetEffectiveEditorialSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "editorial/settings/effective")]
        HttpRequestData req, FunctionContext ctx)
    {
        string? market = Query(req, "market");
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Query 'market' is required.");

        string? station = Query(req, "station");
        var stationOption = string.IsNullOrWhiteSpace(station) ? Option<string>.None : Option<string>.Some(station);

        var result = await settingsRepository.GetApplicableAsync(market, stationOption, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        var resolved = EditorialSettings.Resolve(result.Value, market, stationOption);

        return await HttpResponseHelper.OkJsonAsync(
            req, EffectiveEditorialSettingsDto.From(market, station, resolved, speechDurationOptions.Value));
    }

    /// <summary>Stores the settings row at a scope. Values are bounds-checked before they can shape a board.</summary>
    [Function("UpsertEditorialSettings")]
    public async Task<HttpResponseData> UpsertEditorialSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "editorial/settings")]
        HttpRequestData req, FunctionContext ctx)
    {
        var parsed = await HttpRequestParser.ParseAsync<UpsertEditorialSettingsRequest>(req, ctx.CancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        var r = parsed.Value;
        string scopeId = EditorialSettings.NormalizeScopeId(r.Scope, r.ScopeId);
        if (r.Scope != EditorialScope.Global && string.IsNullOrWhiteSpace(scopeId))
            return await HttpResponseHelper.BadRequestAsync(req, "ScopeId is required for Market/Station scope.");
        if (r.Scope != EditorialScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = r.ToSettings(scopeId, now, UpdatedBy(req));
        var validation = settings.Validate();
        if (validation.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, validation.Error);

        var save = await settingsRepository.SaveAsync(settings, ctx.CancellationToken);
        if (save.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, save.Error);

        logger.LogInformation("[Editorial] Settings saved at {Scope}:{ScopeId} by {User}",
            settings.Scope, settings.ScopeId, settings.UpdatedBy);
        return await HttpResponseHelper.OkJsonAsync(req, EditorialSettingsDto.From(settings));
    }

    /// <summary>Clears the row, reverting that scope to the level below.</summary>
    [Function("DeleteEditorialSettings")]
    public async Task<HttpResponseData> DeleteEditorialSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "editorial/settings")]
        HttpRequestData req, FunctionContext ctx)
    {
        if (!TryReadScope(req, out var scope, out string scopeId, out string? scopeError))
            return await HttpResponseHelper.BadRequestAsync(req, scopeError!);

        var result = await settingsRepository.DeleteAsync(scope, scopeId, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        logger.LogInformation("[Editorial] Settings cleared at {Scope}:{ScopeId} by {User}",
            scope, scopeId, UpdatedBy(req));
        return await HttpResponseHelper.OkJsonAsync(
            req, new { Deleted = "settings", Scope = scope.ToString(), ScopeId = scopeId });
    }

    private static string UpdatedBy(HttpRequestData req) =>
        ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
}
