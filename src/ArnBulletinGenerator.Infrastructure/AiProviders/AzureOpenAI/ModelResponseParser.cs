using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using OpenAI.Responses;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Parses the MODEL'S OWN output content — the review/safety/attribution/director JSON the
///     model wrote as its answer. Extracting that output text from the Responses API's own
///     envelope (status/incomplete/refusal) is now the SDK's job (<see cref="ResponseResult" />
///     typed properties), not hand-rolled JSON walking — see <see cref="LlmScriptGenerator" />.
/// </summary>
internal static class ModelResponseParser
{
    internal static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        string singleLine = text.ReplaceLineEndings(" ");
        return singleLine.Length <= maxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    /// <summary>
    ///     True when the response stopped early purely because it ran out of output tokens —
    ///     the one case <see cref="LlmScriptGenerator" /> retries with a larger budget rather
    ///     than failing the call outright.
    /// </summary>
    internal static bool IsTokenExhausted(ResponseResult result)
    {
        return result.Status == ResponseStatus.Incomplete
               && result.IncompleteStatusDetails?.Reason == ResponseIncompleteStatusReason.MaxOutputTokens;
    }

    /// <summary>
    ///     The model's refusal text, if the Responses API safety layer refused to answer
    ///     (distinct from Arn's own <c>ReviewScriptSafetyAsync</c> — this is upstream of it).
    /// </summary>
    internal static string? ExtractRefusalText(ResponseResult result)
    {
        foreach (var item in result.OutputItems)
        {
            if (item is not MessageResponseItem message)
                continue;
            foreach (var part in message.Content)
                if (!string.IsNullOrWhiteSpace(part.Refusal))
                    return part.Refusal;
        }

        return null;
    }

    internal static string BuildResponseDiagnostic(ResponseResult result)
    {
        var parts = new List<string> { $"status={result.Status}" };
        if (result.IncompleteStatusDetails?.Reason is { } reason)
            parts.Add($"reason={reason}");
        if (ExtractRefusalText(result) is { } refusal)
            parts.Add($"refusal={Truncate(refusal, 120)}");
        if (result.Error is { } error)
            parts.Add($"error={error.Code}: {Truncate(error.Message, 120)}");
        return string.Join(", ", parts);
    }

    internal static ScriptReviewResult ParseReviewResult(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return ScriptReviewResult.Rejected("Review returned empty response.");

        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ScriptReviewResult.Rejected(response);

            var root = document.RootElement;
            string verdict = root.TryGetProperty("verdict", out var verdictElement) &&
                             verdictElement.ValueKind == JsonValueKind.String
                ? verdictElement.GetString() ?? string.Empty
                : string.Empty;

            // The verdict is DERIVED from the issues, not taken on the model's word. A reviewer
            // running at reasoning=none has nowhere to think except the output, and the schema used
            // to make it name the verdict and the defect BEFORE writing a word of analysis — so it
            // committed, then reasoned, and sometimes reasoned its way back. Dev adelaide,
            // 2026-08-26, returned NEEDS_REVISION / WRONG_LOCATION whose own reason ended
            // "Actually the location is supported; no contradiction exists." That rejection cost a
            // refine call and a re-review to undo a defect the reviewer had already withdrawn.
            //
            // The schema now puts `reason` before `defect` and `issues` before `verdict` so the
            // analysis precedes the conclusion; this makes the remaining contradiction
            // unrepresentable. A rejection has to point at something: no issue, no rejection.
            bool saidApproved =
                string.Equals(verdict, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verdict, "APPROVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verdict, "ACCEPT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verdict, "PASS", StringComparison.OrdinalIgnoreCase);

            // The legacy prose fallbacks inside ParseIssues salvage a REJECTION expressed in an old
            // shape; they must not manufacture one out of an approval's commentary. They were
            // previously unreachable on the approved path because the verdict short-circuited above.
            var issues = ParseIssues(root, response, includeLegacyProse: !saidApproved);

            if (issues.Count > 0)
                return new ScriptReviewResult(false, issues);
            if (saidApproved || verdict.Length > 0)
                return ScriptReviewResult.Approved();

            // No verdict field AND no issues: the response told us nothing, so it is not a pass.
            return ScriptReviewResult.Rejected(response);
        }
        catch (JsonException)
        {
            bool approved = response.Contains("Approved", StringComparison.OrdinalIgnoreCase)
                            && !response.Contains("not approved", StringComparison.OrdinalIgnoreCase)
                            && !response.Contains("reject", StringComparison.OrdinalIgnoreCase)
                            && !response.Contains("revise", StringComparison.OrdinalIgnoreCase);
            return approved
                ? ScriptReviewResult.Approved()
                : ScriptReviewResult.Rejected(response);
        }
    }

    private static List<ReviewIssue> ParseIssues(
        JsonElement root, string rawResponse, bool includeLegacyProse = true)
    {
        var result = new List<ReviewIssue>();

        if (root.TryGetProperty("issues", out var issuesElement) && issuesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in issuesElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var issue = ParseStructuredIssueObject(item);
                    if (issue is not null)
                        result.Add(issue);
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    string text = item.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Legacy prose string in issues array: treat as unresolved sentence defect (widen to script)
                        result.Add(ReviewIssue.ForUnresolvedSentence("LEGACY_REVIEW", text));
                    }
                }
            }
        }

        // Fallbacks for non-standard or legacy properties present alongside or instead of issues
        if (!includeLegacyProse)
            return result;

        var legacyProse = new List<string>();
        CollectStringArray(root, "reasoning", legacyProse);
        CollectStringProperty(root, "reason", legacyProse);
        CollectStringProperty(root, "message", legacyProse);
        CollectStringProperty(root, "feedback", legacyProse);

        foreach (string prose in legacyProse.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!result.Any(i => string.Equals(i.Reason, prose, StringComparison.Ordinal)))
                result.Add(ReviewIssue.ForUnresolvedSentence("LEGACY_REVIEW", prose));
        }

        return result;
    }

    private static ReviewIssue? ParseStructuredIssueObject(JsonElement element)
    {
        string defect = GetStringProperty(element, "defect", "SEMANTIC_DEFECT");
        string reason = GetStringProperty(element, "reason", string.Empty);
        string? quote = GetOptionalStringProperty(element, "quote");
        string? road = GetOptionalStringProperty(element, "road");

        if (string.IsNullOrWhiteSpace(reason))
        {
            // Fallback: construct reason from quote and road if reason was missing
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(quote)) parts.Add($"\"{quote}\"");
            if (!string.IsNullOrWhiteSpace(road)) parts.Add(road);
            reason = parts.Count > 0 ? string.Join(" — ", parts) : defect;
        }

        // Parse order defensively
        int? order = null;
        if (element.TryGetProperty("order", out var orderElement))
        {
            if (orderElement.ValueKind == JsonValueKind.Number && orderElement.TryGetInt32(out int parsedOrder))
            {
                if (parsedOrder > 0)
                    order = parsedOrder;
            }
            else if (orderElement.ValueKind == JsonValueKind.String && int.TryParse(orderElement.GetString(), out int stringOrder))
            {
                if (stringOrder > 0)
                    order = stringOrder;
            }
        }

        // Parse scope
        RefineScope scope = RefineScope.Sentence;
        if (element.TryGetProperty("scope", out var scopeElement) && scopeElement.ValueKind == JsonValueKind.String)
        {
            string scopeStr = scopeElement.GetString() ?? string.Empty;
            if (string.Equals(scopeStr, "script", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scopeStr, "global", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scopeStr, "all", StringComparison.OrdinalIgnoreCase))
            {
                scope = RefineScope.Script;
            }
        }

        // Whole-read defects or missing order must be Script scope / unresolved
        if (scope == RefineScope.Script || order is null)
        {
            return order.HasValue && scope == RefineScope.Sentence
                ? ReviewIssue.ForSentence(order.Value, defect, reason)
                : scope == RefineScope.Script
                    ? ReviewIssue.ForScript(defect, reason)
                    : ReviewIssue.ForUnresolvedSentence(defect, reason);
        }

        return ReviewIssue.ForSentence(order.Value, defect, reason);
    }

    private static string GetStringProperty(JsonElement element, string propName, string defaultValue)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            string? val = prop.GetString();
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }
        return defaultValue;
    }

    private static string? GetOptionalStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            string? val = prop.GetString();
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }
        return null;
    }

    internal static void CollectStringArray(JsonElement root, string property, List<string> destination)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            string? text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                destination.Add(text);
        }
    }

    internal static void CollectStringProperty(JsonElement root, string property, List<string> destination)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return;

        string? text = value.GetString();
        if (!string.IsNullOrWhiteSpace(text))
            destination.Add(text);
    }
}
