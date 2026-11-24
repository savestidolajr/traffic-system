using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The review cockpit's pronunciation fix: one word → respelling correction against the
///     dictionary a given bulletin aired with, minting a new dictionary version.
///     <para>
///         This route exists so the fix is an OPERATOR action. It is addressed by JOB, so the
///         caller's market scope can be enforced (<see cref="MarketAccessGuard" />) — an operator
///         may only correct a word on a station they are assigned to. The library-wide
///         <c>PUT /pronunciation-dictionaries/{id}</c> stays Admin: a library item is shared
///         across stations and carries no market of its own to scope against.
///     </para>
/// </summary>
public sealed class JobPronunciationHttpTrigger(
    IJobPronunciationService pronunciation,
    IMarketAccessService marketAccess)
{
    [Function("AddJobPronunciationRule")]
    public async Task<HttpResponseData> AddRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/pronunciation-rules")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var parsed = await HttpRequestParser.ParseAsync<AddJobPronunciationRuleRequestDto>(req, ctx.CancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        var dto = parsed.Value;
        if (string.IsNullOrWhiteSpace(dto.Word))
            return await HttpResponseHelper.BadRequestAsync(req, "A word is required.");
        if (string.IsNullOrWhiteSpace(dto.Replacement))
            return await HttpResponseHelper.BadRequestAsync(req, "A replacement spelling is required.");

        var result = await pronunciation.AddAliasRuleAsync(
            jobId, dto.Word, dto.Replacement, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);

        if (result.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(
                req, result.Error, result.FailureMetadata,
                (r, e) => e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                          e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase) ||
                          e.Contains("must be", StringComparison.OrdinalIgnoreCase)
                    ? HttpResponseHelper.BadRequestAsync(r, e)
                    : e == JobPronunciationService.NoDictionaryOnJob
                        ? HttpResponseHelper.ConflictAsync(r, e)
                        : HttpResponseHelper.InternalErrorAsync(r, e),
                extraNotFoundNeedle: "no longer in the pronunciation library");

        var dictionary = PronunciationDictionariesHttpTrigger.ToDto(result.Value.Dictionary);
        return await HttpResponseHelper.OkJsonAsync(req, new JobPronunciationRuleResponseDto(
            dictionary.Id, Describe(result.Value.Outcome), dictionary));
    }

    /// <summary>Wire spelling of the outcome — the console tells the operator what its click did.</summary>
    private static string Describe(AliasEditOutcome outcome) => outcome switch
    {
        AliasEditOutcome.Added => "added",
        AliasEditOutcome.Changed => "changed",
        _ => "alreadyCorrect"
    };
}

/// <summary>Arrives PascalCase — the SPA pascalizes bodies before sending.</summary>
public sealed record AddJobPronunciationRuleRequestDto(string? Word, string? Replacement);

/// <summary>
///     <c>Id</c> is the dictionary's library id, duplicated at the top level so the activity
///     audit's response-body fallback files this change under the dictionary it changed rather
///     than the job it was made from (see <c>ActivityLoggingMiddleware</c>).
/// </summary>
public sealed record JobPronunciationRuleResponseDto(
    string Id, string Outcome, PronunciationDictionaryDto Dictionary);
