using ArnBulletinGenerator.Workflow.Contracts;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Workflow.Hosting;

/// <summary>
///     Registers the workflow seam. Nothing here edits or replaces the deployed pipeline: a workflow
///     is selected by id, so trialling a new architecture is a configuration change and reverting one
///     is the same change backwards.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <param name="workflowId">Which registered <see cref="IBulletinWorkflow" /> to resolve.</param>
    /// <param name="projectEndpoint">Foundry project, required only when <paramref name="agentByRole" /> is set.</param>
    /// <param name="agentByRole">
    ///     Roles to serve from a published Foundry agent. Empty sends every role to the raw model,
    ///     which is faster to change but produces no traces in the Foundry portal.
    /// </param>
    public static IServiceCollection AddBulletinWorkflows(
        this IServiceCollection services,
        string endpoint,
        string model,
        string workflowId = "staged-v1",
        TokenCredential? credential = null,
        string? projectEndpoint = null,
        IReadOnlyDictionary<string, string>? agentByRole = null,
        string? fallbackModel = null,
        IReadOnlyDictionary<string, string>? fallbackAgentByRole = null,
        AzureOpenAiCallBudgetOptions? callBudget = null)
    {
        AzureOpenAiCallBudgetOptions effectiveBudget = callBudget ?? new();
        services.AddSingleton(new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = endpoint,
            Model = model,
            FallbackModel = fallbackModel,
            ProjectEndpoint = projectEndpoint,
            AgentByRole = agentByRole
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            FallbackAgentByRole = fallbackAgentByRole
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CallBudget = effectiveBudget
        });
        services.AddSingleton(credential ?? new DefaultAzureCredential());
        services.AddHttpClient<AzureOpenAiWorkflowLlm>(
            client => client.Timeout = effectiveBudget.Overall);

        services.AddSingleton<IWorkflowLlm>(sp => sp.GetRequiredService<AzureOpenAiWorkflowLlm>());
        services.AddSingleton<IDurationOracle>(_ => new WordRateDurationOracle());

        services.AddSingleton<IBulletinWorkflow>(sp => new StagedBulletinWorkflow(
            sp.GetRequiredService<IWorkflowLlm>(),
            sp.GetRequiredService<IDurationOracle>()));

        // Registered alongside staged, not instead of it. Until now conversational-v1 could only be
        // constructed by the live-test harness, so "select it by id" was true of one workflow only
        // and the selector would have thrown for the other.
        services.AddSingleton<IBulletinWorkflow>(sp => new ConversationalBulletinWorkflow(
            sp.GetRequiredService<IWorkflowLlm>(),
            sp.GetRequiredService<IDurationOracle>()));

        services.AddSingleton<WorkflowSelector>(sp => new WorkflowSelector(
            sp.GetServices<IBulletinWorkflow>().ToList(), workflowId));

        return services;
    }
}

/// <summary>
///     Resolves the configured workflow by id and fails loudly when it is missing. A silent fallback
///     to a different implementation would make an experiment's results unattributable.
/// </summary>
public sealed class WorkflowSelector(IReadOnlyList<IBulletinWorkflow> workflows, string selectedId)
{
    public IReadOnlyList<IBulletinWorkflow> Available => workflows;

    public IBulletinWorkflow Selected =>
        workflows.FirstOrDefault(w => string.Equals(w.Id, selectedId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"No workflow registered with id '{selectedId}'. Registered: {string.Join(", ", workflows.Select(w => w.Id))}.");
}
