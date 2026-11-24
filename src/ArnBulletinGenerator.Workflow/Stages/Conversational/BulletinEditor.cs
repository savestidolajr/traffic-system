using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     Reviews the conversational read as a senior radio editor. Unlike the composer, this role sees
///     the compact source data as well as the menu, so it can check truth without carrying the old
///     machine-checkable citation apparatus through every stage.
/// </summary>
public sealed class BulletinEditor(IWorkflowLlm llm)
{
    public async Task<EditorVerdict> ReviewAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        string read,
        TraceRecorder trace,
        CancellationToken cancellationToken = default)
    {
        const string role = "edit";
        string systemPrompt = PromptLibrary.System(role);
        string userPrompt = RenderPrompt(request, input, briefs, read);

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            LlmReply reply;

            try
            {
                reply = await llm.CompleteAsync(
                        role,
                        systemPrompt,
                        userPrompt,
                        PromptLibrary.Schema(role),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                reply = LlmReply.Failure(exception.Message);
                trace.LlmCall("Edit", new LlmCallRecord(
                    role,
                    reply.Model,
                    systemPrompt,
                    userPrompt,
                    reply.Text,
                    stopwatch.Elapsed.TotalMilliseconds,
                    reply.PromptTokens,
                    reply.CompletionTokens,
                    exception.Message,
                    reply.CachedInputTokens,
                    reply.ReasoningTokens,
                    reply.Attempt,
                    reply.StatusCode,
                    reply.ViaFallback,
                    reply.DeadlineUtc,
                    reply.AttemptElapsedMs,
                    reply.Stalled,
                    reply.ResponseId));
                trace.Failure("Edit", "Editor LLM call failed", exception.Message);
                EditorVerdict failedCallVerdict = EditorVerdict.Unreviewed(
                    $"The editor transport failed: {exception.Message}");
                RecordVerdict(trace, failedCallVerdict);
                return failedCallVerdict;
            }

            stopwatch.Stop();
            trace.LlmCall("Edit", new LlmCallRecord(
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
                string reason = reply.Error ?? "The model returned a failure.";
                trace.Failure("Edit", "Editor LLM call failed", reason);
                EditorVerdict failedReplyVerdict = EditorVerdict.Unreviewed(reason);
                RecordVerdict(trace, failedReplyVerdict);
                return failedReplyVerdict;
            }

            try
            {
                EditorVerdict verdict = ParseVerdict(reply.Text);
                RecordVerdict(trace, verdict);
                return verdict;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt < 2)
                {
                    trace.Decision("Edit", "Editor response was invalid; retrying once",
                        new Dictionary<string, string> { ["reason"] = exception.Message });
                    continue;
                }

                trace.Failure("Edit", "Editor response could not be parsed", exception.Message);
                EditorVerdict malformedVerdict = EditorVerdict.Unreviewed(exception.Message);
                RecordVerdict(trace, malformedVerdict);
                return malformedVerdict;
            }
        }

        return EditorVerdict.Unreviewed("The editor did not return a usable verdict.");
    }

    private static string RenderPrompt(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        string read)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(SourceDataRenderer.RenderBrief(request, null));
        prompt.AppendLine();
        prompt.AppendLine("SOURCE DATA:");
        prompt.AppendLine(SourceDataRenderer.Render(input));
        prompt.AppendLine();
        prompt.AppendLine("WRITER'S STORY BRIEFS:");
        prompt.AppendLine(briefs.Render(input));
        prompt.AppendLine();
        prompt.AppendLine("FINISHED READ (sentence numbers are for feedback only):");

        string trimmedRead = read.Trim();
        if (trimmedRead.Length == 0)
        {
            prompt.AppendLine("(empty read)");
        }
        else
        {
            string[] sentences = Regex.Split(trimmedRead, @"(?<=[.!?])\s+");
            for (int index = 0; index < sentences.Length; index++)
                prompt.AppendLine($"{index + 1}. {sentences[index].Trim()}");
        }

        return prompt.ToString();
    }

    private static EditorVerdict ParseVerdict(string rawResponse)
    {
        string response = StripCodeFence(rawResponse);
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Editor response must be a JSON object.");

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "approved", StringComparison.Ordinal)
                && !string.Equals(property.Name, "feedback", StringComparison.Ordinal))
                throw new JsonException($"Editor response contains unknown property '{property.Name}'.");
        }

        if (!root.TryGetProperty("approved", out JsonElement approvedElement))
            throw new JsonException("Editor response is missing required property 'approved'.");
        if (approvedElement.ValueKind != JsonValueKind.True && approvedElement.ValueKind != JsonValueKind.False)
            throw new JsonException("Editor approved value must be boolean.");

        if (!root.TryGetProperty("feedback", out JsonElement feedbackElement))
            throw new JsonException("Editor response is missing required property 'feedback'.");
        if (feedbackElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Editor feedback value must be an array.");

        List<string> feedback = [];
        foreach (JsonElement item in feedbackElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new JsonException("Editor feedback entries must be strings.");
            string note = item.GetString()?.Trim() ?? string.Empty;
            if (note.Length > 0)
                feedback.Add(note);
        }

        return new EditorVerdict(approvedElement.GetBoolean(), feedback);
    }

    private static void RecordVerdict(TraceRecorder trace, EditorVerdict verdict)
    {
        if (verdict.IsUnreviewed)
        {
            trace.Decision("Edit", "Editor verdict unavailable: read left unreviewed",
                new Dictionary<string, string> { ["reason"] = string.Join("; ", verdict.Feedback) });
            return;
        }

        string summary = verdict.Approved
            ? "Editor approved the bulletin."
            : "Editor rejected the bulletin: " + string.Join("; ", verdict.Feedback);
        trace.Check("Edit", summary, verdict.Approved);
    }

    private static string StripCodeFence(string raw)
    {
        string text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        int firstLineBreak = text.IndexOf('\n');
        text = firstLineBreak >= 0 ? text[(firstLineBreak + 1)..] : text[3..];
        string withoutClosingFence = text.TrimEnd();
        if (withoutClosingFence.EndsWith("```", StringComparison.Ordinal))
            withoutClosingFence = withoutClosingFence[..^3];
        return withoutClosingFence.Trim();
    }
}
