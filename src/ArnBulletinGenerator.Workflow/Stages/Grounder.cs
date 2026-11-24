using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Checks atomic entailment after composition. Keeping this as a separate, narrow pass prevents a
///     reviewer from smuggling style preferences into the factual gate, and preserves the distinction
///     between a claim that is unsupported and one that the evidence contradicts.
/// </summary>
public sealed class Grounder(IWorkflowLlm llm)
{
    public async Task<GroundingReport> GroundAsync(
        WorkflowRequest request,
        NormalizedInput input,
        ComposedRead read,
        TraceRecorder trace,
        CancellationToken cancellationToken = default)
    {
        const string stage = "Ground";
        const string role = "ground";
        trace.StageStart(stage, $"Ground {read.Sentences.Count} finished sentences");

        string systemPrompt = PromptLibrary.System(role);
        string userPrompt = BuildPrompt(request, input, read);
        string jsonSchema = PromptLibrary.Schema(role);

        var stopwatch = Stopwatch.StartNew();
        LlmReply reply;
        try
        {
            reply = await llm.CompleteAsync(
                role,
                systemPrompt,
                userPrompt,
                jsonSchema,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            string error = exception.Message;
            trace.LlmCall(stage, new LlmCallRecord(
                role,
                string.Empty,
                systemPrompt,
                userPrompt,
                string.Empty,
                stopwatch.Elapsed.TotalMilliseconds,
                Error: error,
                Attempt: 1));
            trace.Failure(stage, "Ground LLM call failed", error);
            trace.StageEnd(stage, "Ground failed", stopwatch.Elapsed.TotalMilliseconds);
            return GroundingReport.Empty;
        }

        trace.LlmCall(stage, new LlmCallRecord(
            role,
            reply.Model,
            systemPrompt,
            userPrompt,
            reply.Text,
            stopwatch.Elapsed.TotalMilliseconds,
            reply.PromptTokens,
            reply.CompletionTokens,
            reply.Error,
            reply.CachedInputTokens,
            reply.ReasoningTokens,
            reply.Attempt,
            reply.StatusCode,
            reply.ViaFallback,
            reply.DeadlineUtc,
            reply.AttemptElapsedMs,
            reply.Stalled,
            reply.ResponseId));

        if (!reply.IsSuccess)
        {
            string error = reply.Error ?? "The grounder returned an unsuccessful response.";
            trace.Failure(stage, "Ground LLM call failed", error);
            trace.StageEnd(stage, "Ground failed", stopwatch.Elapsed.TotalMilliseconds);
            return GroundingReport.Empty;
        }

        GroundingReport report;
        try
        {
            report = Parse(reply.Text, input, read);
        }
        catch (Exception exception)
        {
            string error = exception.Message;
            trace.Failure(stage, "Could not parse grounder response", error);
            trace.StageEnd(stage, "Ground failed", stopwatch.Elapsed.TotalMilliseconds);
            return GroundingReport.Empty;
        }

        trace.Check(
            stage,
            $"Ground claims: {report.Claims.Count}; unsupported={report.Unsupported.Count}; contradicted={report.Contradicted.Count}",
            report.IsClean);
        trace.Artifact(stage, "Claims", BuildArtifact(report));
        trace.StageEnd(stage, $"Ground complete: {report.Claims.Count} claims", stopwatch.Elapsed.TotalMilliseconds);
        return report;
    }

    private static string BuildPrompt(WorkflowRequest request, NormalizedInput input, ComposedRead read)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("FINISHED READ");
        prompt.AppendLine("The numbers below are the only valid 1-based sentence orders.");
        if (read.Sentences.Count == 0)
        {
            prompt.AppendLine("(empty read)");
        }
        else
        {
            for (int index = 0; index < read.Sentences.Count; index++)
            {
                prompt.Append(read.Sentences[index].Order)
                    .Append(". ")
                    .AppendLine(read.Sentences[index].Text);
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("EVIDENCE EVENTS");
        if (input.Events.Count == 0)
        {
            prompt.AppendLine("(no events supplied)");
        }
        else
        {
            for (int index = 0; index < input.Events.Count; index++)
            {
                BulletinEvent bulletinEvent = input.Events[index];
                prompt.Append("Event ").Append(index + 1).AppendLine();
                prompt.Append("  event_id: ").AppendLine(JsonSerializer.Serialize(bulletinEvent.EventId));
                prompt.Append("  kind: ").AppendLine(JsonSerializer.Serialize(bulletinEvent.Kind.ToString()));
                prompt.Append("  label: ").AppendLine(JsonSerializer.Serialize(bulletinEvent.Label));
                prompt.AppendLine("  facts:");
                if (bulletinEvent.Facts.Count == 0)
                {
                    prompt.AppendLine("    (none)");
                }
                else
                {
                    foreach (EventFact fact in bulletinEvent.Facts)
                    {
                        prompt.Append("    - id: ").Append(JsonSerializer.Serialize(fact.EvidenceId));
                        prompt.Append("; field: ").Append(JsonSerializer.Serialize(fact.Field));
                        prompt.Append("; value: ").AppendLine(JsonSerializer.Serialize(fact.Value));
                    }
                }
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("AUTHORISED STATION/RUN CONTEXT");
        prompt.Append("market: ").AppendLine(JsonSerializer.Serialize(request.Market));
        prompt.Append("station: ").AppendLine(JsonSerializer.Serialize(request.Station));
        prompt.Append("location: ").AppendLine(JsonSerializer.Serialize(request.Location));
        prompt.Append("job_id: ").AppendLine(JsonSerializer.Serialize(request.JobId));
        prompt.AppendLine("The supplied station/run context is an authorised support namespace, alongside event facts. Do not use world knowledge.");
        prompt.AppendLine();
        prompt.AppendLine("Return only the JSON object required by the ground schema. Report findings, not a verdict.");
        return prompt.ToString();
    }

    private static GroundingReport Parse(string raw, NormalizedInput input, ComposedRead read)
    {
        string json = RemoveMarkdownFence(raw);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(document.RootElement, "claims", out JsonElement claimsElement) ||
            claimsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Grounder response must be an object containing a claims array.");
        }

        HashSet<string> validEvidenceIds = input.Events
            .SelectMany(bulletinEvent => bulletinEvent.Facts)
            .Select(fact => fact.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<int> validSentenceOrders = read.Sentences
            .Select(sentence => sentence.Order)
            .Where(order => order > 0)
            .ToHashSet();

        var claims = new List<AtomicClaim>();

        foreach (JsonElement claimElement in claimsElement.EnumerateArray())
        {
            if (claimElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Every grounder claim must be an object.");
            }

            if (!TryGetProperty(claimElement, "sentenceOrder", out JsonElement orderElement) ||
                !orderElement.TryGetInt32(out int sentenceOrder))
            {
                throw new JsonException("Every grounder claim must have an integer sentenceOrder.");
            }

            if (!TryGetProperty(claimElement, "claim", out JsonElement textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Every grounder claim must have a string claim.");
            }

            string claim = textElement.GetString() ?? string.Empty;
            List<string> support = ParseSupport(claimElement, validEvidenceIds);
            string? note = ParseNote(claimElement);
            ClaimStatus status = ParseStatus(claimElement);

            if (validSentenceOrders.Contains(sentenceOrder))
            {
                claims.Add(new AtomicClaim(sentenceOrder, claim, status, support, note));
            }
        }

        return new GroundingReport(claims);
    }

    private static List<string> ParseSupport(JsonElement claimElement, HashSet<string> validEvidenceIds)
    {
        var support = new List<string>();
        if (!TryGetProperty(claimElement, "support", out JsonElement supportElement) ||
            supportElement.ValueKind != JsonValueKind.Array)
        {
            return support;
        }

        foreach (JsonElement idElement in supportElement.EnumerateArray())
        {
            if (idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? id = idElement.GetString();
            if (id is not null && validEvidenceIds.Contains(id) && !support.Contains(id, StringComparer.Ordinal))
            {
                support.Add(id);
            }
        }

        return support;
    }

    private static string? ParseNote(JsonElement claimElement)
    {
        if (!TryGetProperty(claimElement, "note", out JsonElement noteElement) ||
            noteElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return noteElement.ValueKind == JsonValueKind.String ? noteElement.GetString() : null;
    }

    private static ClaimStatus ParseStatus(JsonElement claimElement)
    {
        if (!TryGetProperty(claimElement, "status", out JsonElement statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
        {
            return ClaimStatus.NotSupported;
        }

        string normalized = (statusElement.GetString() ?? string.Empty)
            .Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized switch
        {
            "SUPPORTED" => ClaimStatus.Supported,
            "CONTRADICTED" => ClaimStatus.Contradicted,
            "NOTSUPPORTED" => ClaimStatus.NotSupported,
            _ => ClaimStatus.NotSupported
        };
    }

    private static IReadOnlyDictionary<string, string> BuildArtifact(GroundingReport report)
    {
        return report.Claims
            .Select((claim, index) => new KeyValuePair<string, string>(
                $"{claim.SentenceOrder}.{index + 1} {StatusName(claim.Status)}",
                $"{claim.Claim}  <- {string.Join(", ", claim.Support)}"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string StatusName(ClaimStatus status) => status switch
    {
        ClaimStatus.Supported => "SUPPORTED",
        ClaimStatus.Contradicted => "CONTRADICTED",
        _ => "NOT_SUPPORTED"
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string RemoveMarkdownFence(string raw)
    {
        string text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        int firstNewline = text.IndexOf('\n');
        if (firstNewline < 0 || !text.EndsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        return text[(firstNewline + 1)..^3].Trim();
    }
}
