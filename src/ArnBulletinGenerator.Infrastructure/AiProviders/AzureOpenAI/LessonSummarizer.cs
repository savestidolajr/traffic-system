using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Isolated single-call LLM behind <see cref="ILessonSummarizer" />: given one published
///     operator edit and the station's stored lessons, returns a structured
///     create/reinforce/supersede/none verdict. Uses the raw-model structured path (strict JSON
///     schema) under the "Lesson" model role — no Foundry agent, mirroring the Safety role's
///     isolation from the drafting pipeline.
/// </summary>
public sealed class LessonSummarizer(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureAIOptions> azureOptions,
    TimeProvider timeProvider,
    ILogger<LlmScriptGenerator> logger,
    ILlmCallCapture llmCallCapture,
    TokenCredential? tokenCredential = null) : ILessonSummarizer
{
    private const int MaxOutputTokens = 512;

    /// <summary>Verbatim system instructions; changes here move the learning bar.</summary>
    private const string SystemPrompt =
        "You analyse one radio traffic-bulletin script edit that a station operator made and then "
        + "approved for air. Decide whether the edit reveals a REUSABLE station wording preference. "
        + "You must generalise, never copy specifics: no road names, suburbs, incident details, "
        + "times, or numbers may appear in the instruction. Never learn from: pronunciation "
        + "respellings, one-off factual corrections, added/removed incidents, audio tags like "
        + "[slow], or edits explainable by slot length. You are given the station's existing "
        + "lessons; prefer reinforcing an existing lesson that this edit supports (action "
        + "\"reinforce\" with its id), or superseding one it contradicts (action \"supersede\" "
        + "with its id and a replacement instruction). Only \"create\" for a genuinely new "
        + "preference. If the edit is not safely generalisable, return action \"none\". The "
        + "instruction must be one imperative sentence of 20-300 characters addressed to a script "
        + "writer. Confidence is your probability (0-1) that a station editor would endorse the "
        + "instruction as a standing rule.";

    // Strict schema: every field required, no additionals. targetLessonId is an empty string
    // when not applicable — strict schemas cannot mark optionals.
    private static readonly BinaryData Schema = BinaryData.FromString(
        """
        {"type":"object","properties":{
          "action":{"type":"string","enum":["none","create","reinforce","supersede"]},
          "category":{"type":"string","enum":["PriorityOrdering","ContentSelection","WordingStyle","StructureRepetition"]},
          "instruction":{"type":"string"},
          "targetLessonId":{"type":"string"},
          "confidence":{"type":"number"},
          "reason":{"type":"string"}},
          "required":["action","category","instruction","targetLessonId","confidence","reason"],
          "additionalProperties":false}
        """);

    // Built directly (not DI-registered) for the same reason as LlmScriptGenerator's transport:
    // ResponsesTransport is internal and this class's constructor is public.
    private readonly ResponsesTransport _transport =
        new(httpClientFactory, azureOptions, timeProvider, logger, llmCallCapture, tokenCredential);

    public async Task<Result<LessonSummaryResult>> SummarizeAsync(
        LessonSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
            return Result<LessonSummaryResult>.Failure(authHeader.Error);

        string userPrompt = BuildUserPrompt(request);
        var result = await _transport.DispatchStructuredLlmAsync(
            "Lesson", userPrompt, MaxOutputTokens, cancellationToken, authHeader.Value,
            hasProducerFeedback: false, jobId: request.JobId, jsonSchema: Schema,
            systemInstructionsOverride: SystemPrompt, modelRole: "Lesson");

        if (result.IsFailure)
            return Result<LessonSummaryResult>.Failure(result.Error);

        return Parse(result.Value!);
    }

    private static string BuildUserPrompt(LessonSummaryRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("STATION: ").Append(PromptSafe.Sanitize(request.Station)).Append('\n');
        sb.Append("EXISTING LESSONS:\n");
        if (request.ExistingLessons.Count == 0)
        {
            sb.Append("(none)\n");
        }
        else
        {
            foreach (var lesson in request.ExistingLessons)
                sb.Append(lesson.Id).Append(" | ").Append(lesson.Status)
                    .Append(" | seen ").Append(lesson.OccurrenceCount).Append("x | [")
                    .Append(lesson.Category).Append("] ")
                    .Append(PromptSafe.Sanitize(lesson.Text)).Append('\n');
        }

        sb.Append("\nORIGINAL SCRIPT:\n").Append(PromptSafe.Sanitize(request.OriginalScript));
        sb.Append("\n\nPUBLISHED SCRIPT:\n").Append(PromptSafe.Sanitize(request.PublishedScript));
        sb.Append("\n\nCHANGES:\n").Append(PromptSafe.Sanitize(request.RenderedDiff));
        return sb.ToString();
    }

    /// <summary>Any malformed field is a Failure — a misread verdict must never write a lesson.</summary>
    private static Result<LessonSummaryResult> Parse(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;

            if (!root.TryGetProperty("action", out var actionProp)
                || actionProp.ValueKind != JsonValueKind.String
                || !Enum.TryParse<LessonSummaryAction>(actionProp.GetString(), true, out var action))
                return Result<LessonSummaryResult>.Failure(
                    $"Lesson summary response has no valid 'action': {ModelResponseParser.Truncate(response, 200)}");

            if (!root.TryGetProperty("category", out var categoryProp)
                || categoryProp.ValueKind != JsonValueKind.String
                || !Enum.TryParse<FutureLessonCategory>(categoryProp.GetString(), false, out var category))
                return Result<LessonSummaryResult>.Failure(
                    $"Lesson summary response has no valid 'category': {ModelResponseParser.Truncate(response, 200)}");

            if (!root.TryGetProperty("instruction", out var instructionProp)
                || instructionProp.ValueKind != JsonValueKind.String)
                return Result<LessonSummaryResult>.Failure(
                    $"Lesson summary response has no 'instruction': {ModelResponseParser.Truncate(response, 200)}");

            if (!root.TryGetProperty("confidence", out var confidenceProp)
                || confidenceProp.ValueKind != JsonValueKind.Number)
                return Result<LessonSummaryResult>.Failure(
                    $"Lesson summary response has no numeric 'confidence': {ModelResponseParser.Truncate(response, 200)}");

            string? targetLessonId =
                root.TryGetProperty("targetLessonId", out var targetProp)
                && targetProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(targetProp.GetString())
                    ? targetProp.GetString()
                    : null;

            string reason = root.TryGetProperty("reason", out var reasonProp)
                            && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            return Result<LessonSummaryResult>.Success(new LessonSummaryResult(
                action, category, instructionProp.GetString() ?? string.Empty,
                targetLessonId, confidenceProp.GetDouble(), reason));
        }
        catch (JsonException ex)
        {
            return Result<LessonSummaryResult>.Failure(
                $"Lesson summary response was not valid JSON ({ex.Message}): {ModelResponseParser.Truncate(response, 200)}");
        }
    }
}
