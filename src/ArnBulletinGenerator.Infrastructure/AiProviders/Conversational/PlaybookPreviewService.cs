using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;

/// <summary>Request for a non-persistent preview of a station playbook overlay.</summary>
public sealed record PlaybookPreviewRequest(
    string Role,
    string Market,
    PlaybookOverlay Overlay,
    string PrincipalId = "anonymous",
    string Revision = "");

/// <summary>Generated playbook preview before the HTTP response is shaped.</summary>
public sealed record PlaybookPreviewGeneration(string Script, double? PredictedSeconds, IReadOnlyList<string> Notes);

/// <summary>The read produced by a draft playbook overlay against a recent replayable job.</summary>
public sealed record PlaybookPreview(
    string Script,
    string? BaselineScript,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> Notes,
    double? PredictedSeconds);

public interface IPlaybookPreviewService
{
    Task<Result<PlaybookPreview>> PreviewAsync(
        PlaybookPreviewRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Generates one conversational read from a persisted generator-input snapshot. This service
///     deliberately has no write, queue, audio, or job-lifecycle dependency: an audition must not
///     mutate the bulletin it is based on.
/// </summary>
public sealed class PlaybookPreviewService(
    IJobRepository jobRepository,
    IPlaybookPreviewGenerator generator,
    IShippedPlaybookProvider shippedPlaybookProvider,
    TimeProvider timeProvider,
    ILogger<PlaybookPreviewService> logger,
    IInteractiveWorkGate? interactiveWorkGate = null) : IPlaybookPreviewService
{
    private readonly IInteractiveWorkGate? _interactiveWorkGate = interactiveWorkGate;
    private const string ComposeRole = "compose2";
    private const int JobPageLimit = 1000;

    public async Task<Result<PlaybookPreview>> PreviewAsync(
        PlaybookPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Role, ComposeRole, StringComparison.OrdinalIgnoreCase))
            return Result<PlaybookPreview>.Failure(
                $"Unsupported playbook role '{request.Role}'. Only '{ComposeRole}' can be previewed.");
        if (string.IsNullOrWhiteSpace(request.Market))
            return Result<PlaybookPreview>.Failure("Market is required.");
        if (request.Overlay is null)
            return Result<PlaybookPreview>.Failure("Overlay is required.");

        var key = new InteractiveWorkKey(
            string.IsNullOrWhiteSpace(request.PrincipalId) ? "anonymous" : request.PrincipalId,
            "preview",
            request.Revision,
            JsonSerializer.Serialize(request.Overlay),
            Settings: request.Role,
            Style: request.Market);
        return _interactiveWorkGate is null
            ? await PreviewCoreAsync(request, cancellationToken)
            : await _interactiveWorkGate.ExecuteLlmAsync(
                key,
                gateCancellationToken => PreviewCoreAsync(request, gateCancellationToken),
                cancellationToken);
    }

    private async Task<Result<PlaybookPreview>> PreviewCoreAsync(
        PlaybookPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var shipped = shippedPlaybookProvider.GetDefault(ComposeRole);
        if (shipped.IsFailure)
            return Result<PlaybookPreview>.Failure(shipped.Error);
        var applied = shipped.Value.Apply(request.Overlay);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var jobs = await jobRepository.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UnixEpoch, now, JobPageLimit,
                Market: request.Market.Trim()), cancellationToken);
        if (jobs.IsFailure)
            return Result<PlaybookPreview>.Failure(jobs.Error);

        Job? source = jobs.Value.FirstOrDefault(job => job.GeneratorInput is not null);
        if (source is null)
            return Result<PlaybookPreview>.Failure(
                $"No replayable job exists for market '{request.Market.Trim()}'.");

        // ToContext rebuilds the exact evidence shape persisted for the job. Only the overlay and
        // trace label differ; no object from the source aggregate is changed or saved.
        BroadcastWithTraffic context = source.GeneratorInput!.ToContext($"{source.JobId}-preview") with
        {
            Playbook = request.Overlay
        };

        var generated = await generator.GenerateAsync(context, cancellationToken);
        if (generated.IsFailure)
            return Result<PlaybookPreview>.Failure(generated.Error);

        string? baseline = source.ScriptText.HasValue ? source.ScriptText.Value : null;
        logger.LogInformation("[PlaybookPreview] Market={Market} Job={JobId} Refused={Refused}",
            request.Market.Trim(), source.JobId, applied.Refused.Count);

        return Result<PlaybookPreview>.Success(new PlaybookPreview(
            generated.Value.Script,
            baseline,
            applied.Refused,
            generated.Value.Notes,
            generated.Value.PredictedSeconds));
    }
}

public interface IPlaybookPreviewGenerator
{
    Task<Result<PlaybookPreviewGeneration>> GenerateAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Infrastructure adapter for the conversational workflow. Preview owns a separate workflow
///     instance with one revision maximum, avoiding the live generator's longer editor loop while
///     retaining its shared request mapping and calibrated duration oracle.
/// </summary>
public sealed class ConversationalPlaybookPreviewGenerator(
    IWorkflowLlm llm,
    IDurationPredictor predictor,
    IDurationOracle fallbackOracle,
    IOptions<BroadcastOptions> broadcastOptions) : IPlaybookPreviewGenerator
{
    public async Task<Result<PlaybookPreviewGeneration>> GenerateAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken = default)
    {
        IDurationOracle duration = new CalibratedDurationOracle(
            predictor,
            new PredictionContext
            {
                VoiceId = context.VoiceId.GetValueOr(broadcastOptions.Value.DefaultVoiceId),
                ModelId = context.Model.GetValueOr(string.Empty),
                Speed = context.VoiceSettings.HasValue ? context.VoiceSettings.Value.Speed : 1.0
            },
            fallbackOracle);

        // MaxRevisions=1 keeps the preview to the initial compose/review plus at most one rewrite.
        var workflow = new ConversationalBulletinWorkflow(
            llm, duration, new ConversationalWorkflowOptions { MaxRevisions = 1 });
        WorkflowRequest workflowRequest = ConversationalScriptGenerator.BuildRequest(context, duration);
        WorkflowOutcome outcome = await workflow.RunAsync(workflowRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!outcome.IsSuccess || string.IsNullOrWhiteSpace(outcome.Script))
            return Result<PlaybookPreviewGeneration>.Failure(
                outcome.Error ?? "Conversational workflow returned no script.");

        var notes = outcome.Trace.Outcome is { } status && status != "succeeded"
            ? (IReadOnlyList<string>)[$"Workflow outcome: {status}."]
            : [];
        return Result<PlaybookPreviewGeneration>.Success(new PlaybookPreviewGeneration(
            outcome.Script, duration.PredictSeconds(outcome.Script), notes));
    }
}
