using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Web;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Contracts;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.ConfigTransfer;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The admin config-transfer surface: export the whole editable configuration as one JSON
///     bundle (for editing by hand or with an AI assistant), validate an edited bundle as a
///     dry-run diff report, and apply it. All routes are Admin-only (see RouteRoleMatrix).
///
///     Applying is dry-run by default — a caller must send <c>?dryRun=false</c> to change
///     anything. When <see cref="ConfigImportOptions.RequireSecondApprover" /> is on (prod),
///     an apply parks the bundle as a pending import that a DIFFERENT admin must approve:
///     the same two-person posture as production deploys. Approval re-validates against
///     current state, so a bundle that went stale while parked is refused, not misapplied.
/// </summary>
public sealed class ConfigTransferHttpTrigger(
    ConfigExportBuilder exportBuilder,
    ConfigImportService importService,
    IConfigImportStore importStore,
    IOptions<ConfigImportOptions> importOptions,
    TimeProvider timeProvider,
    ILogger<ConfigTransferHttpTrigger> logger)
{
    private static readonly Lazy<string> SchemaJson = new(LoadSchema);

    /// <summary>
    ///     Hard cap on an import body. A full real bundle (every library, every schedule) is far
    ///     smaller than this; anything past it is DoS-shaped, not a legitimate config edit.
    /// </summary>
    private const int MaxImportBodyBytes = 2 * 1024 * 1024; // 2 MB

    [Function("ExportConfig")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/export")]
        HttpRequestData req, FunctionContext ctx)
    {
        var sections = ConfigSections.ParseFilter(Query(req, "sections"));
        if (sections.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, sections.Error);

        var schemaUrl = new Uri(req.Url, "/api/config/schema").ToString();
        var bundle = await exportBuilder.BuildAsync(sections.Value, schemaUrl, ctx.CancellationToken);
        if (bundle.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, bundle.Error);

        logger.LogInformation("[ConfigTransfer] Config exported ({Sections}) by {User}",
            string.Join(",", sections.Value), Actor(req).Name);
        return await HttpResponseHelper.OkJsonAsync(req, bundle.Value);
    }

    [Function("GetConfigSchema")]
    public async Task<HttpResponseData> Schema(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/schema")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/schema+json; charset=utf-8");
        await response.WriteStringAsync(SchemaJson.Value);
        return response;
    }

    [Function("ImportConfig")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/import")]
        HttpRequestData req, FunctionContext ctx)
    {
        // Dry-run unless explicitly disabled: an import must always be a deliberate act.
        bool dryRun = !string.Equals(Query(req, "dryRun"), "false", StringComparison.OrdinalIgnoreCase);

        var bodyRead = await ReadBoundedBodyAsync(req, MaxImportBodyBytes, ctx.CancellationToken);
        if (bodyRead.TooLarge)
            return await PayloadTooLargeAsync(req,
                $"Import request body exceeds the {MaxImportBodyBytes / (1024 * 1024)} MB limit.");
        if (bodyRead.Error is not null)
            return await HttpResponseHelper.BadRequestAsync(req, bodyRead.Error);

        ConfigBundleDto? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ConfigBundleDto>(bodyRead.Body!, JsonSerializationDefaults.Request);
        }
        catch (JsonException)
        {
            return await HttpResponseHelper.BadRequestAsync(req, "Invalid JSON format");
        }

        if (bundle is null)
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.RequestBodyRequired);

        // Detect typo'd top-level section names (e.g. 'Schedule' vs 'Schedules') that plain
        // deserialization would otherwise silently drop.
        var unknownSections = ConfigImportService.DetectUnknownSections(bodyRead.Body!);

        var actor = Actor(req);
        var plan = await importService.PlanAsync(bundle, actor.Name, ctx.CancellationToken, unknownSections);
        if (plan.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, plan.Error);

        bool requiresApproval = importOptions.Value.RequireSecondApprover;

        if (plan.Value.HasErrors)
        {
            // Validation failed — nothing applies (all-or-nothing). The report carries the errors.
            var errorReport = plan.Value.ToReport(dryRun, requiresApproval, applied: false);
            return await JsonAsync(req, HttpStatusCode.BadRequest, errorReport);
        }

        if (dryRun)
            return await HttpResponseHelper.OkJsonAsync(req,
                plan.Value.ToReport(dryRun: true, requiresApproval, applied: false));

        if (requiresApproval)
        {
            var report = plan.Value.ToReport(dryRun: false, requiresApproval: true, applied: false);
            var pending = new PendingConfigImport(
                Guid.NewGuid().ToString("N"),
                actor.Name,
                actor.ObjectId,
                timeProvider.GetUtcNow().UtcDateTime,
                JsonSerializer.Serialize(report, JsonSerializationDefaults.Response),
                bodyRead.Body!);

            var saved = await importStore.SavePendingAsync(pending, ctx.CancellationToken);
            if (saved.IsFailure)
                return await HttpResponseHelper.InternalErrorAsync(req, saved.Error);

            logger.LogInformation(
                "[ConfigTransfer] Import parked as pending {Id} by {User} — second approver required",
                pending.Id, actor.Name);
            return await HttpResponseHelper.AcceptedJsonAsync(req,
                new ConfigImportAcceptedDto(pending.Id, report));
        }

        var applied = await importService.ApplyAsync(plan.Value, requiresApproval: false, ctx.CancellationToken);
        logger.LogInformation("[ConfigTransfer] Config imported by {User}: {Creates} created, {Updates} updated",
            actor.Name, applied.Creates, applied.Updates);
        return await HttpResponseHelper.OkJsonAsync(req, applied);
    }

    [Function("ListPendingConfigImports")]
    public async Task<HttpResponseData> ListPending(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/imports/pending")]
        HttpRequestData req, FunctionContext ctx)
    {
        var pending = await importStore.ListPendingAsync(ctx.CancellationToken);
        if (pending.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, pending.Error);

        var items = pending.Value.Select(p => new PendingConfigImportDto(
            p.Id, p.RequestedBy, p.RequestedAtUtc.ToString("o"), DeserializeReport(p.ReportJson))).ToList();
        return await HttpResponseHelper.OkJsonAsync(req, new PendingConfigImportsListDto(items, items.Count));
    }

    [Function("ApprovePendingConfigImport")]
    public async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/imports/pending/{id}/approve")]
        HttpRequestData req, string id, FunctionContext ctx)
    {
        if (StorageKey.RejectIfInvalid(req, "id", id) is { } badId)
            return await badId;

        var pendingResult = await importStore.GetPendingAsync(id, ctx.CancellationToken);
        if (pendingResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, pendingResult.Error);
        if (!pendingResult.Value.HasValue)
            return await HttpResponseHelper.NotFoundAsync(req, $"Pending import '{id}' not found.");

        var pending = pendingResult.Value.Value;
        var actor = Actor(req);

        // The two-person gate: the requester can never approve their own import.
        if (!string.IsNullOrWhiteSpace(pending.RequestedByObjectId)
            && string.Equals(pending.RequestedByObjectId, actor.ObjectId, StringComparison.OrdinalIgnoreCase))
            return await HttpResponseHelper.ForbiddenAsync(req,
                "A pending import must be approved by a different admin than the one who requested it.");

        ConfigBundleDto? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ConfigBundleDto>(pending.BundleJson, JsonSerializationDefaults.Request);
        }
        catch (JsonException)
        {
            bundle = null;
        }

        if (bundle is null)
            return await HttpResponseHelper.InternalErrorAsync(req, $"Pending import '{id}' payload is unreadable.");

        // Re-plan against CURRENT state: the environment may have drifted since the request.
        var unknownSections = ConfigImportService.DetectUnknownSections(pending.BundleJson);
        var plan = await importService.PlanAsync(bundle, actor.Name, ctx.CancellationToken, unknownSections);
        if (plan.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, plan.Error);

        if (plan.Value.HasErrors)
        {
            logger.LogWarning("[ConfigTransfer] Pending import {Id} no longer valid at approval time", id);
            return await JsonAsync(req, HttpStatusCode.Conflict,
                plan.Value.ToReport(dryRun: false, requiresApproval: true, applied: false));
        }

        // Second half of the two-person gate: the re-plan must match the report the requester
        // submitted for review (the one the UI shows in the confirm dialog). A plan that merely
        // CHANGES SHAPE — Create → Update, or a different diff — would apply with semantics the
        // approver never saw, e.g. silently overwriting a newer interactive edit (reproduced on
        // dev 2026-08-05). Refuse with 409 naming the drift; the requester re-submits for a fresh
        // review. The errors branch above already refuses stale plans; this catches the silent ones.
        var storedReport = DeserializeReport(pending.ReportJson);
        if (storedReport is null)
        {
            logger.LogWarning("[ConfigTransfer] Pending import {Id} has an unreadable stored report", id);
            return await HttpResponseHelper.ConflictAsync(req,
                $"Pending import '{id}' has an unreadable stored review report; re-import the bundle " +
                "to re-submit it for approval.");
        }

        var freshReport = plan.Value.ToReport(dryRun: false, requiresApproval: true, applied: false);
        if (!storedReport.IsEquivalentTo(freshReport))
        {
            logger.LogWarning("[ConfigTransfer] Pending import {Id} drifted since it was submitted for approval", id);
            return await HttpResponseHelper.ConflictAsync(req,
                $"The plan changed since it was submitted for approval: {storedReport.DescribeDrift(freshReport)}. " +
                "Reject and re-submit the bundle for a fresh review.");
        }

        var applied = await importService.ApplyAsync(plan.Value, requiresApproval: true, ctx.CancellationToken);
        var resolved = await importStore.ResolveAsync(id, "applied", actor.Name, actor.ObjectId, ctx.CancellationToken);
        if (resolved.IsFailure)
            logger.LogError("[ConfigTransfer] Import {Id} applied but archiving failed: {Error}", id, resolved.Error);

        logger.LogInformation(
            "[ConfigTransfer] Pending import {Id} (requested by {RequestedBy}) approved and applied by {ApprovedBy}",
            id, pending.RequestedBy, actor.Name);
        return await HttpResponseHelper.OkJsonAsync(req, applied);
    }

    [Function("RejectPendingConfigImport")]
    public async Task<HttpResponseData> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/imports/pending/{id}/reject")]
        HttpRequestData req, string id, FunctionContext ctx)
    {
        if (StorageKey.RejectIfInvalid(req, "id", id) is { } badId)
            return await badId;

        var pendingResult = await importStore.GetPendingAsync(id, ctx.CancellationToken);
        if (pendingResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, pendingResult.Error);
        if (!pendingResult.Value.HasValue)
            return await HttpResponseHelper.NotFoundAsync(req, $"Pending import '{id}' not found.");

        var actor = Actor(req);
        var resolved = await importStore.ResolveAsync(id, "rejected", actor.Name, actor.ObjectId, ctx.CancellationToken);
        if (resolved.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, resolved.Error);

        logger.LogInformation("[ConfigTransfer] Pending import {Id} rejected by {User}", id, actor.Name);
        return await HttpResponseHelper.OkJsonAsync(req, new { rejected = id });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (string Name, string ObjectId) Actor(HttpRequestData req)
    {
        var principal = ClientPrincipal.Parse(req);
        return (
            principal?.Name is { Length: > 0 } name ? name : "unknown",
            principal?.ObjectId ?? string.Empty);
    }

    private static string? Query(HttpRequestData req, string key)
        => HttpUtility.ParseQueryString(req.Url.Query).Get(key);

    private static ConfigImportReportDto? DeserializeReport(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ConfigImportReportDto>(json, JsonSerializationDefaults.Request);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonSerializationDefaults.Response));
        return response;
    }

    private static Task<HttpResponseData> PayloadTooLargeAsync(HttpRequestData req, string message)
    {
        return JsonAsync(req, HttpStatusCode.RequestEntityTooLarge,
            new ErrorResponse(message, null, (int)HttpStatusCode.RequestEntityTooLarge));
    }

    /// <summary>
    ///     Reads the request body up to <paramref name="maxBytes" />, rejecting early on an
    ///     over-large declared Content-Length and aborting mid-stream if the actual byte count
    ///     exceeds the cap (covers chunked/absent Content-Length) — never buffers past the limit.
    /// </summary>
    private static async Task<BoundedBodyRead> ReadBoundedBodyAsync(
        HttpRequestData req, int maxBytes, CancellationToken ct)
    {
        if (req.Headers.TryGetValues("Content-Length", out var lengths)
            && long.TryParse(lengths.FirstOrDefault(), out long declaredLength)
            && declaredLength > maxBytes)
            return new BoundedBodyRead(TooLarge: true, Error: null, Body: null);

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int total = 0;
            int read;
            while ((read = await req.Body.ReadAsync(chunk, 0, chunk.Length, ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    return new BoundedBodyRead(TooLarge: true, Error: null, Body: null);
                await buffer.WriteAsync(chunk, 0, read, ct);
            }

            buffer.Position = 0;
            using var reader = new StreamReader(buffer);
            string text = await reader.ReadToEndAsync(ct);
            return string.IsNullOrWhiteSpace(text)
                ? new BoundedBodyRead(false, ErrorMessages.Http.RequestBodyRequired, null)
                : new BoundedBodyRead(false, null, text);
        }
        catch (OperationCanceledException)
        {
            return new BoundedBodyRead(false, "Request cancelled while reading body", null);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return new BoundedBodyRead(false, $"Failed to read request body: {ex.GetType().Name}", null);
        }
    }

    private sealed record BoundedBodyRead(bool TooLarge, string? Error, string? Body);

    private static string LoadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "ArnBulletinGenerator.Functions.Resources.config-bundle.schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
