using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The shared bookend library: named, versioned intro/outro pairs stations read around every
///     bulletin. CRUD + version restore delegate to <see cref="IBookendService" />; preview
///     resolves BOTH lines' <c>{{token}}</c> placeholders against a station's built-ins and
///     overrides so an operator can read the exact sentences that would air — pure resolution, no
///     persistence (mirrors the delivery-style library; a bookend is one pair, so there is no
///     kind segment).
/// </summary>
public sealed class BookendsHttpTrigger(
    IBookendService bookends,
    ILogger<BookendsHttpTrigger> logger)
{
    [Function("ListBookends")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bookends")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var query = VersionedLibraryListQueryParser.Parse(req);
        if (query.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, query.Error);

        Result<VersionedLibraryListPage<Bookend>>? result =
            await bookends.ListAsync(query.Value, ctx.CancellationToken);
        if (result is null)
        {
            // Keep lightweight/legacy service doubles usable while the paged contract rolls out.
            var legacy = await bookends.ListAsync(ctx.CancellationToken);
            if (legacy is null)
                return await HttpResponseHelper.InternalErrorAsync(req, "Bookend list failed.");
            result = legacy.IsFailure
                ? legacy.PropagateFailure<VersionedLibraryListPage<Bookend>>()
                : Result<VersionedLibraryListPage<Bookend>>.Success(
                    new VersionedLibraryListPage<Bookend>(legacy.Value, legacy.Value.Count));
        }
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var items = result.Value.Items.Select(ToDto).ToList();
        if (!query.Value.IsPaged)
            return await HttpResponseHelper.OkJsonAsync(req, new BookendsListResponseDto(items));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = items,
            Total = result.Value.Total,
            NextCursor = result.Value.NextCursor
        });
    }

    [Function("CreateBookend")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookends")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<CreateBookendRequest>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        var bodyError = ValidateBookendBody(dto.Name, dto.Description, dto.IntroText, dto.OutroText);
        if (bodyError is not null)
            return await HttpResponseHelper.BadRequestAsync(req, bodyError);

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var existingForDuplicateCheck = await bookends.ListAsync(ctx.CancellationToken);
            if (existingForDuplicateCheck.IsSuccess && existingForDuplicateCheck.Value.Any(b =>
                    string.Equals(b.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
                return await HttpResponseHelper.ConflictAsync(req,
                    $"A bookend named '{dto.Name}' already exists.");
        }

        var input = new CreateBookendInput(dto.Name ?? string.Empty, dto.Description,
            dto.IntroText ?? string.Empty, dto.OutroText ?? string.Empty,
            dto.ChangeNote, ClientPrincipal.Parse(req)?.Name);
        var result = await bookends.CreateAsync(input, ctx.CancellationToken);
        return await Respond(req, result);
    }

    // Distinct literal route so it always wins over bookends/{id}.
    [Function("PreviewBookend")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookends/preview")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<PreviewBookendRequest>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        var validation = new BookendTemplate(dto.IntroText ?? string.Empty, dto.OutroText ?? string.Empty).Validate();
        if (validation.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, validation.Error);

        var marketId = dto.MarketId ?? string.Empty;
        var station = dto.Station ?? string.Empty;
        var introResolution = BookendTokenResolver.Resolve(dto.IntroText ?? string.Empty, marketId, station, dto.Tokens);
        var outroResolution = BookendTokenResolver.Resolve(dto.OutroText ?? string.Empty, marketId, station, dto.Tokens);
        // A token referenced by both lines is reported once — the operator fixes it in one place.
        var unresolved = introResolution.UnresolvedTokens
            .Concat(outroResolution.UnresolvedTokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var values = BookendTokenResolver.BuildValues(marketId, station, dto.Tokens);

        logger.LogInformation("[BookendPreview] Resolved pair preview ({Unresolved} unresolved tokens)",
            unresolved.Count);

        return await HttpResponseHelper.OkJsonAsync(req,
            new PreviewBookendResponse(introResolution.Text, outroResolution.Text, unresolved, values));
    }

    [Function("GetBookend")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bookends/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A bookend id is required.");

        var result = await bookends.GetAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UpdateBookend")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "bookends/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A bookend id is required.");

        var parse = await HttpRequestParser.ParseAsync<UpdateBookendRequest>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        var bodyError = ValidateBookendBody(dto.Name, dto.Description, dto.IntroText, dto.OutroText);
        if (bodyError is not null)
            return await HttpResponseHelper.BadRequestAsync(req, bodyError);

        var input = new UpdateBookendInput(id, dto.Name ?? string.Empty, dto.Description,
            dto.IntroText ?? string.Empty, dto.OutroText ?? string.Empty,
            dto.ChangeNote, ClientPrincipal.Parse(req)?.Name);
        var result = await bookends.UpdateAsync(input, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("RestoreBookendVersion")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookends/{id}/versions/{version}/restore")]
        HttpRequestData req,
        string id, string version, FunctionContext ctx)
    {
        if (!int.TryParse(version, out var versionNumber))
            return await HttpResponseHelper.BadRequestAsync(req, "Version must be an integer.");

        var result = await bookends.RestoreVersionAsync(id, versionNumber,
            ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("ArchiveBookend")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookends/{id}/archive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A bookend id is required.");

        var result = await bookends.ArchiveAsync(id, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UnarchiveBookend")]
    public async Task<HttpResponseData> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookends/{id}/unarchive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A bookend id is required.");

        var result = await bookends.UnarchiveAsync(id, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("DeleteBookend")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "bookends/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A bookend id is required.");

        var result = await bookends.DeleteAsync(id, ctx.CancellationToken);
        if (result.IsFailure)
            // Typed mapping: an in-use item returns a Conflict whose message names the
            // stations, and message matching alone would send that to a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        return HttpResponseHelper.NoContent(req);
    }

    /// <summary>Name/description/text guards shared by Create and Update (mirrors the delivery-style trigger).</summary>
    private static string? ValidateBookendBody(string? name, string? description, string? introText, string? outroText)
    {
        if ((name?.Length ?? 0) > ConfigValidationLimits.BookendNameMax)
            return $"Name must be at most {ConfigValidationLimits.BookendNameMax} characters.";
        if ((description?.Length ?? 0) > ConfigValidationLimits.BookendDescriptionMax)
            return $"Description must be at most {ConfigValidationLimits.BookendDescriptionMax} characters.";

        var validation = new BookendTemplate(introText ?? string.Empty, outroText ?? string.Empty).Validate();
        return validation.IsFailure ? validation.Error : null;
    }

    private static async Task<HttpResponseData> Respond(HttpRequestData req, Result<Bookend> result)
    {
        if (result.IsFailure)
            // A TYPED disposition (e.g. an over-ceiling single version that VersionHistoryTrimmer
            // marks InvalidInput) must map to 400 — message matching alone cannot see it and would
            // send a permanent client-input error to a 500. The existing required/Invalid message
            // fallback is kept in front of the server-error default.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) =>
                    e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                    e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                        ? HttpResponseHelper.BadRequestAsync(r, e)
                        : HttpResponseHelper.InternalErrorAsync(r, e));

        return await HttpResponseHelper.OkJsonAsync(req, ToDto(result.Value));
    }

    private static BookendDto ToDto(Bookend bookend)
    {
        var versions = bookend.Versions.Select(v => new BookendVersionDto(
            v.Version,
            v.Template.IntroText,
            v.Template.OutroText,
            v.ChangeNote,
            v.CreatedAt,
            v.CreatedBy)).ToList();

        // Distinct union of the tokens both lines reference, intro first, in first-appearance
        // order — a token in both lines is listed once.
        var tokens = new List<string>();
        foreach (var token in BookendTokenResolver.ExtractTokens(bookend.CurrentTemplate.IntroText)
                     .Concat(BookendTokenResolver.ExtractTokens(bookend.CurrentTemplate.OutroText)))
            if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                tokens.Add(token);

        return new BookendDto(
            bookend.Id,
            bookend.Name,
            bookend.Description,
            bookend.Status,
            bookend.CurrentTemplate.IntroText,
            bookend.CurrentTemplate.OutroText,
            bookend.CurrentVersion,
            bookend.CreatedAt,
            bookend.UpdatedAt,
            bookend.CreatedBy,
            bookend.AssignedStations.ToList(),
            tokens,
            versions);
    }
}

// ─── Request DTOs (arrive PascalCase — the SPA pascalizes bodies before sending) ───

public sealed record CreateBookendRequest(
    string? Name = null,
    string? Description = null,
    string? IntroText = null,
    string? OutroText = null,
    string? ChangeNote = null);

public sealed record UpdateBookendRequest(
    string? Name = null,
    string? Description = null,
    string? IntroText = null,
    string? OutroText = null,
    string? ChangeNote = null);

public sealed record PreviewBookendRequest(
    string? IntroText = null,
    string? OutroText = null,
    string? MarketId = null,
    string? Station = null,
    IReadOnlyDictionary<string, string>? Tokens = null);

// ─── Response DTOs (PascalCase; the SPA's Zod schemas camelize on receipt) ───

public sealed record BookendVersionDto(
    int Version,
    string IntroText,
    string OutroText,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy);

public sealed record BookendDto(
    string Id,
    string Name,
    string? Description,
    string Status,
    string IntroText,
    string OutroText,
    int CurrentVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<string> AssignedStations,
    IReadOnlyList<string> Tokens,
    IReadOnlyList<BookendVersionDto> Versions);

public sealed record BookendsListResponseDto(
    IReadOnlyList<BookendDto> Items);

public sealed record PreviewBookendResponse(
    string ResolvedIntro,
    string ResolvedOutro,
    IReadOnlyList<string> UnresolvedTokens,
    IReadOnlyDictionary<string, string> Values);
