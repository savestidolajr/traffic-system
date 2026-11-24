using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.Tests.Fakes;

/// <summary>
///     A deterministic <see cref="IWorkflowLlm" /> that answers from a script keyed by role.
///     <para>
///         Lets the whole workflow — planning, fitting, composition, verification and the duration
///         loop — be exercised offline, at zero cost and with no network, which is what makes the
///         structural behaviour testable at all. A real model is only needed to judge the WRITING.
///     </para>
/// </summary>
public sealed class ScriptedWorkflowLlm : IWorkflowLlm
{
    private readonly Dictionary<string, Queue<ScriptedReply>> _byRole = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call made, in order, for assertions about how a workflow drove the model.</summary>
    public List<ScriptedCall> Calls { get; } = [];

    /// <summary>Queues one response for a role. Successive calls to the same role dequeue in order.</summary>
    public ScriptedWorkflowLlm Reply(string role, string json) =>
        Queue(role, new ScriptedReply(json, null));

    /// <summary>Queues one response with a provider id so a test can exercise thread continuation.</summary>
    public ScriptedWorkflowLlm ReplyWithResponseId(string role, string response, string responseId) =>
        Queue(role, new ScriptedReply(response, responseId));

    private ScriptedWorkflowLlm Queue(string role, ScriptedReply reply)
    {
        if (!_byRole.TryGetValue(role, out var queue))
            _byRole[role] = queue = new Queue<ScriptedReply>();
        queue.Enqueue(reply);
        return this;
    }

    /// <summary>Queues a transport failure for a role.</summary>
    public ScriptedWorkflowLlm Fail(string role, string error) =>
        Reply(role, $"__FAIL__{error}");

    public Task<LlmReply> CompleteAsync(
        string role, string systemPrompt, string userPrompt, string? jsonSchema,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new ScriptedCall(role, systemPrompt, userPrompt, jsonSchema));
        return Next(role, userPrompt, "reply");
    }

    /// <summary>
    ///     A follow-up turn dequeues from the same per-role script as a fresh call, preserving the
    ///     exact call order while allowing tests to assert that the composer retained its thread.
    /// </summary>
    public Task<LlmReply> ContinueAsync(
        string role, string previousResponseId, string followUpPrompt,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new ScriptedCall(role, previousResponseId, followUpPrompt, null));
        return Next(role, followUpPrompt, "continuation");
    }

    private Task<LlmReply> Next(string role, string prompt, string callKind)
    {
        if (!_byRole.TryGetValue(role, out var queue) || queue.Count == 0)
        {
            return Task.FromResult(LlmReply.Failure(
                $"ScriptedWorkflowLlm has no queued {callKind} for role '{role}' (call {Calls.Count})",
                "scripted"));
        }

        ScriptedReply next = queue.Dequeue();
        return Task.FromResult(next.Text.StartsWith("__FAIL__", StringComparison.Ordinal)
            ? LlmReply.Failure(next.Text["__FAIL__".Length..], "scripted")
            : new LlmReply(next.Text, "scripted", PromptTokens: prompt.Length / 4,
                CompletionTokens: next.Text.Length / 4, ResponseId: next.ResponseId));
    }

    public int CallsTo(string role) =>
        Calls.Count(c => string.Equals(c.Role, role, StringComparison.OrdinalIgnoreCase));
}

public sealed record ScriptedCall(string Role, string SystemPrompt, string UserPrompt, string? Schema);
public sealed record ScriptedReply(string Text, string? ResponseId);
