using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>Auditions a draft compose2 playbook overlay against the latest replayable bulletin.</summary>
public sealed class PlaybookPreviewHttpTrigger(IPlaybookPreviewService previewService)
{
    [Function("PreviewPlaybook")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playbooks/preview")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parsed = await HttpRequestParser.ParseAsync<PlaybookPreviewRequestDto>(req, ctx.CancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        PlaybookPreviewRequestDto dto = parsed.Value;
        if (string.IsNullOrWhiteSpace(dto.Role))
            return await HttpResponseHelper.BadRequestAsync(req, "Role is required.");
        if (!string.Equals(dto.Role, "compose2", StringComparison.OrdinalIgnoreCase))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Unsupported playbook role. Only 'compose2' can be previewed.");
        if (string.IsNullOrWhiteSpace(dto.Market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");
        if (dto.Overlay is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Overlay is required.");

        var result = await previewService.PreviewAsync(
            new PlaybookPreviewRequest(
                dto.Role,
                dto.Market.Trim(),
                dto.Overlay.ToDomain(),
                ClientPrincipal.Parse(req)?.ObjectId ?? "anonymous"));
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(
                req, result.Error, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e),
                extraNotFoundNeedle: "No replayable job exists");

        return await HttpResponseHelper.OkJsonAsync(req, result.Value);
    }
}

/// <summary>PascalCase wire request accepted by <c>POST /api/playbooks/preview</c>.</summary>
public sealed record PlaybookPreviewRequestDto(
    string Role,
    string Market,
    PlaybookOverlayDto? Overlay);
