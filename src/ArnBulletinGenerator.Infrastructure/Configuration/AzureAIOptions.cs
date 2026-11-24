using System.ComponentModel.DataAnnotations;
using ArnBulletinGenerator.Workflow.Hosting;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Azure OpenAI/Foundry connection settings for script generation: endpoint, model, auth
///     mode, per-attempt timeout, and optional Foundry agent overrides for the draft and polish
///     stages.
/// </summary>
public sealed class AzureAIOptions
{
    public const string SectionName = "AiSettings:AzureAI";

    [Required(ErrorMessage = "Azure Open AI Endpoint is required")]
    [Url(ErrorMessage = "Azure Open AI Endpoint must be a valid URL")]
    public string Endpoint { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure Open AI Model configuration is required")]
    public string Model { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    /// <summary>
    ///     Per-attempt HTTP timeout (seconds) for Azure OpenAI calls. Applied by
    ///     <see cref="ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI.RateLimitRetryHandler"/>
    ///     for EACH attempt (not across the whole retry chain) — a single hung attempt is cut at this
    ///     bound and retried, instead of blocking a pipeline slot for the client's 5-minute overall
    ///     timeout, which remains as the absolute backstop for the whole retry loop.
    /// </summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    ///     Azure AI Speech endpoint for the pre-approval audio review (fast transcription,
    ///     api-version 2024-11-15) — e.g. "https://ibp-aue-dev-fdy-app01.cognitiveservices.azure.com".
    ///     The AIServices account already exposes it; whisper is NOT deployable in australiaeast,
    ///     and fast transcription supports en-AU natively. Empty = audio pre-review disabled.
    /// </summary>
    public string? SpeechEndpoint { get; set; }

    /// <summary>
    ///     Overrides the per-model Responses API <c>reasoning.effort</c> when set
    ///     ("none" | "minimal" | "low" | "medium" | "high"). Null = derive from the model family.
    ///     gpt-5.x defaults to "none" (no planning); "low" markedly improves multi-constraint
    ///     adherence (item-dropping, numeric economy, magnitude honesty) at a small latency cost.
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    ///     When non-empty, the Draft stage calls this Foundry prompt agent by name instead of the
    ///     raw model. The agent carries the system prompt baked in — the rendered draft template is
    ///     sent as the user input. The Foundry project endpoint is derived from
    ///     <see cref="Endpoint" />.
    /// </summary>
    public string? DraftAgentName { get; set; }

    /// <summary>
    ///     The Foundry project endpoint for agent calls (e.g.
    ///     "https://ibp-aue-dev-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator").
    ///     Required when <see cref="DraftAgentName"/> is set — the agent responses endpoint is
    ///     {FoundryProjectEndpoint}/agents/{DraftAgentName}/endpoint/protocols/openai/responses.
    /// </summary>
    public string? FoundryProjectEndpoint { get; set; }

    /// <summary>
    ///     When non-empty, the Review stage calls this Foundry prompt agent by name instead of
    ///     the raw model. The agent carries the review system prompt AND its reasoning effort
    ///     ("low" — A/B-critical) baked into its definition. Uses <see cref="FoundryProjectEndpoint"/>.
    ///     Empty → the raw-model path with the on-disk prompts/review.md.
    /// </summary>
    public string? ReviewAgentName { get; set; }

    /// <summary>When non-empty, the sentence-scoped refine stage calls this Foundry prompt agent instead of the raw model.</summary>
    public string? RefineSentenceAgentName { get; set; }

    /// <summary>
    ///     When non-empty, the content-safety stage calls this Foundry prompt agent instead of the
    ///     raw model. NOTE: the safety gate is compliance-reviewed — a change to this agent's baked-in
    ///     instructions in the Foundry portal is a change to the broadcast-standards bar and must be
    ///     treated with the same review rigour as a code change. Uses <see cref="FoundryProjectEndpoint"/>.
    /// </summary>
    public string? SafetyAgentName { get; set; }

    /// <summary>When non-empty, the Director stage calls this Foundry prompt agent instead of the raw model.</summary>
    public string? DirectorAgentName { get; set; }

    /// <summary>
    ///     When non-empty, the operator-facing delivery and content rewrite roles call this Foundry
    ///     prompt agent instead of the raw model. The agent carries a neutral shim; role behaviour
    ///     remains in the request templates (`delivery-writer.md` and `rewrite.md`).
    /// </summary>
    public string? DeliveryWriterAgentName { get; set; }
    // ── Per-role model deployments ──────────────────────────────────────
    // Each pipeline role can be pinned to its own Azure OpenAI deployment name, so quota/capacity
    // and the underlying model can be tuned independently. Empty overrides fall back to Model.
    public string? DraftModel { get; set; }
    public string? ReviewModel { get; set; }
    public string? RefineModel { get; set; }
    public string? SafetyModel { get; set; }
    public string? DirectorModel { get; set; }
    /// <summary>Deployment for the isolated station-lesson summariser; empty falls back to <see cref="Model"/>.</summary>
    public string? LessonModel { get; set; }

    /// <summary>Resolves the deployment name for a pipeline role, falling back to <see cref="Model"/> when no override is configured.</summary>
    public string ModelFor(string role)
    {
        string? roleModel = role switch
        {
            "Draft" => DraftModel,
            "Review" => ReviewModel,
            "RefineSentence" => RefineModel,
            "Safety" => SafetyModel,
            "Director" => DirectorModel,
            "Lesson" => LessonModel,
            _ => null
        };
        return string.IsNullOrWhiteSpace(roleModel) ? Model : roleModel;
    }

    /// <summary>
    ///     Resolves the Foundry prompt-agent name for a supported pipeline role, or null when the
    ///     role has no agent configured (in which case the caller uses the raw-model path).
    ///     Supported roles are Draft, CitationFix, Review, RefineSentence, Attribute, Safety,
    ///     Director, DeliveryWriter, Rewrite, and Polish.
    /// </summary>
    public string? AgentNameFor(string role) => role switch
    {
        "Draft" => DraftAgentName,
        "Review" => ReviewAgentName,
        "RefineSentence" => RefineSentenceAgentName,
        "Safety" => SafetyAgentName,
        "Director" => DirectorAgentName,
        // Delivery and content rewrites share the neutral, JSON-returning delivery-writer agent.
        // Their role-specific behaviour remains in the request templates.
        "DeliveryWriter" or "Rewrite" => DeliveryWriterAgentName,
        _ => null
    } is { } name && !string.IsNullOrWhiteSpace(name) ? name : null;

    /// <summary>
    ///     Shared retry/deadline limits for one logical Azure OpenAI operation. Raw transport and
    ///     Workflow.Hosting calls use the same instance so retries cannot multiply at a layer
    ///     boundary.
    /// </summary>
    public AzureOpenAiCallBudgetOptions CallBudget { get; set; } = new();
}
