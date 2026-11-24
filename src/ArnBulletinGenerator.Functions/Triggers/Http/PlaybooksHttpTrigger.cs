using System.Net;
using System.Text.Json;
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
///     The shared bulletin-playbook library: named, versioned overlays on the shipped role
///     instructions. CRUD, version restore and status changes delegate to
///     <see cref="IBulletinPlaybookService" />.
/// </summary>
public sealed class PlaybooksHttpTrigger(
    IBulletinPlaybookService playbooks,
    IShippedPlaybookProvider shippedPlaybooks,
    ILogger<PlaybooksHttpTrigger> logger)
{
    [Function("ListPlaybooks")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playbooks")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var query = VersionedLibraryListQueryParser.Parse(req);
        if (query.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, query.Error);

        var result = await playbooks.ListAsync(query.Value, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var items = result.Value.Items.Select(ToDto).ToList();
        if (!query.Value.IsPaged)
            return await HttpResponseHelper.OkJsonAsync(req,
                new BulletinPlaybooksListResponseDto(items, result.Value.Total));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = items,
            Total = result.Value.Total,
            NextCursor = result.Value.NextCursor
        });
    }

    [Function("GetDefaultPlaybook")]
    public async Task<HttpResponseData> Default(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playbooks/default/{role}")]
        HttpRequestData req,
        string role,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(role))
            return await HttpResponseHelper.BadRequestAsync(req, "A playbook role is required.");
        string requestedRole = role.Trim();
        string? normalizedRole = BulletinPlaybook.SupportedRoles.FirstOrDefault(
            supported => string.Equals(supported, requestedRole, StringComparison.OrdinalIgnoreCase));
        if (normalizedRole is null)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Unknown playbook role '{role}'. Supported: {string.Join(", ", BulletinPlaybook.SupportedRoles)}.");

        var shipped = shippedPlaybooks.GetDefault(normalizedRole);
        if (shipped.IsFailure)
        {
            if (shipped.Error.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.OkJsonAsync(req,
                    new DefaultPlaybookResponseDto(normalizedRole, false, []));
            logger.LogError("Invalid default playbook prompt for role {Role}: {Error}",
                normalizedRole, shipped.Error);
            return await HttpResponseHelper.InternalErrorAsync(req,
                "The shipped default playbook is invalid.");
        }

        var blocks = shipped.Value.Blocks.Select(ToBlockDto).ToList();
        return await HttpResponseHelper.OkJsonAsync(req,
            new DefaultPlaybookResponseDto(normalizedRole, blocks.Count > 0, blocks));
    }

    [Function("CreatePlaybook")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playbooks")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<CreatePlaybookRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if (dto.Overlay is null)
            return await HttpResponseHelper.BadRequestAsync(req, "A playbook overlay is required.");

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var existing = await playbooks.ListAsync(ctx.CancellationToken);
            if (existing.IsSuccess && existing.Value.Any(p =>
                    string.Equals(p.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
                return await HttpResponseHelper.ConflictAsync(req,
                    $"A bulletin playbook named '{dto.Name}' already exists.");
        }

        var input = new CreatePlaybookInput(dto.Name ?? string.Empty, dto.Description, dto.Role ?? string.Empty,
            dto.Overlay.ToDomain(), dto.ChangeNote, dto.Id);
        var result = await playbooks.CreateAsync(input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("GetPlaybook")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playbooks/{id}")]
        HttpRequestData req,
        string id,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "default")
            return await HttpResponseHelper.BadRequestAsync(req, "A bulletin playbook id is required.");

        var result = await playbooks.GetAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UpdatePlaybook")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "playbooks/{id}")]
        HttpRequestData req,
        string id,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "default")
            return await HttpResponseHelper.BadRequestAsync(req, "A bulletin playbook id is required.");

        var parse = await HttpRequestParser.ParseAsync<UpdatePlaybookRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if (dto.Overlay is null)
            return await HttpResponseHelper.BadRequestAsync(req, "A playbook overlay is required.");

        var input = new UpdatePlaybookInput(dto.Name ?? string.Empty, dto.Description, dto.Overlay.ToDomain(),
            dto.ChangeNote);
        var result = await playbooks.UpdateAsync(id, input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("RestorePlaybookVersion")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playbooks/{id}/versions/{version}/restore")]
        HttpRequestData req,
        string id,
        string version,
        FunctionContext ctx)
    {
        if (!int.TryParse(version, out var versionNumber))
            return await HttpResponseHelper.BadRequestAsync(req, "Version must be an integer.");

        var result = await playbooks.RestoreVersionAsync(id, versionNumber, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("ArchivePlaybook")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playbooks/{id}/archive")]
        HttpRequestData req,
        string id,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A bulletin playbook id is required.");

        var result = await playbooks.ArchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UnarchivePlaybook")]
    public async Task<HttpResponseData> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playbooks/{id}/unarchive")]
        HttpRequestData req,
        string id,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A bulletin playbook id is required.");

        var result = await playbooks.UnarchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("DeletePlaybook")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playbooks/{id}")]
        HttpRequestData req,
        string id,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "default")
            return await HttpResponseHelper.BadRequestAsync(req, "A bulletin playbook id is required.");

        var result = await playbooks.DeleteAsync(id, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        return HttpResponseHelper.NoContent(req);
    }
    private static async Task<HttpResponseData> Respond(HttpRequestData req, Result<BulletinPlaybook> result)
    {
        if (result.IsFailure)
        {
            const string prefix = "Playbook overlay refused: ";
            if (result.Error.StartsWith(prefix, StringComparison.Ordinal))
            {
                string joined = result.Error[prefix.Length..];
                string[] refused = joined.Split("; ", StringSplitOptions.RemoveEmptyEntries);
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(
                    new { Error = result.Error, Refused = refused }, JsonSerializationDefaults.Response));
                return response;
            }

            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                          e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                    ? HttpResponseHelper.BadRequestAsync(r, e)
                    : HttpResponseHelper.InternalErrorAsync(r, e));
        }

        return await HttpResponseHelper.OkJsonAsync(req, ToDto(result.Value));
    }

    private static BulletinPlaybookDto ToDto(BulletinPlaybook playbook) =>
        new(
            playbook.Id,
            playbook.Name,
            playbook.Description,
            playbook.Role,
            playbook.Status,
            playbook.CurrentVersion,
            ToDto(playbook.CurrentOverlay),
            playbook.CreatedAt.ToString("o"),
            playbook.UpdatedAt.ToString("o"),
            playbook.CreatedBy,
            playbook.AssignedStations.ToList(),
            playbook.Versions.Select(v => new BulletinPlaybookVersionDto(
                v.Version,
                ToDto(v.Overlay),
                v.ChangeNote,
                v.CreatedAt.ToString("o"),
                v.CreatedBy)).ToList());

    private static PlaybookOverlayResponseDto ToDto(PlaybookOverlay overlay) =>
        new(
            overlay.Rewrite.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            overlay.Disable.ToList(),
            overlay.Reorder.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            overlay.Add.Select(ToBlockDto).ToList());
    private static PlaybookBlockResponseDto ToBlockDto(PlaybookBlock block) =>
        new(block.Id, block.Section.ToString(), block.Body, block.Order, block.Locked, block.Enabled);
}

// Request DTOs arrive PascalCase. The SPA pascalizes bodies before sending them.
public sealed record CreatePlaybookRequestDto(
    string? Name = null,
    string? Description = null,
    string? Role = null,
    PlaybookOverlayDto? Overlay = null,
    string? ChangeNote = null,
    string? Id = null);

public sealed record UpdatePlaybookRequestDto(
    string? Name = null,
    string? Description = null,
    PlaybookOverlayDto? Overlay = null,
    string? ChangeNote = null);

public sealed record PlaybookOverlayDto(
    Dictionary<string, string>? Rewrite = null,
    List<string>? Disable = null,
    Dictionary<string, int>? Reorder = null,
    List<PlaybookBlockDto>? Add = null)
{
    public PlaybookOverlay ToDomain() => new()
    {
        Rewrite = Rewrite is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Rewrite, StringComparer.Ordinal),
        Disable = Disable is null
            ? []
            : Disable.ToList(),
        Reorder = Reorder is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(Reorder, StringComparer.Ordinal),
        Add = Add is null ? [] : Add.Select(b => b.ToDomain()).ToList()
    };
}

public sealed record PlaybookBlockDto(
    string? Id = null,
    PlaybookSection Section = PlaybookSection.Role,
    string? Body = null,
    int Order = 0,
    bool Locked = false,
    bool Enabled = true)
{
    public PlaybookBlock ToDomain() => new(Id ?? string.Empty, Section, Body ?? string.Empty, Order, Locked, Enabled);
}

// Response DTOs are PascalCase. The SPA camelizes them on receipt.
public sealed record BulletinPlaybookVersionDto(
    int Version,
    PlaybookOverlayResponseDto Overlay,
    string? ChangeNote,
    string CreatedAt,
    string? CreatedBy);

public sealed record BulletinPlaybookDto(
    string Id,
    string Name,
    string? Description,
    string Role,
    string Status,
    int CurrentVersion,
    PlaybookOverlayResponseDto CurrentOverlay,
    string CreatedAt,
    string UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<string> AssignedStations,
    IReadOnlyList<BulletinPlaybookVersionDto> Versions);

public sealed record BulletinPlaybooksListResponseDto(
    IReadOnlyList<BulletinPlaybookDto> Items,
    int Total);

public sealed record DefaultPlaybookResponseDto(
    string Role,
    bool DefaultAvailable,
    IReadOnlyList<PlaybookBlockResponseDto> Blocks);

public sealed record PlaybookOverlayResponseDto(
    IReadOnlyDictionary<string, string> Rewrite,
    IReadOnlyList<string> Disable,
    IReadOnlyDictionary<string, int> Reorder,
    IReadOnlyList<PlaybookBlockResponseDto> Add);

public sealed record PlaybookBlockResponseDto(
    string Id,
    string Section,
    string Body,
    int Order,
    bool Locked,
    bool Enabled);
