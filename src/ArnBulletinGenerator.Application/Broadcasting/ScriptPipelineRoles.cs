using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     The LLM-facing roles that turn traffic evidence into a broadcast script: draft, review,
///     sentence-scoped patch, direct, safety-review, and content-filter. Bundled because
///     <see cref="BroadcastOrchestrator" /> only ever threads them through as a group into its
///     script-side collaborators (<c>ScriptReviewLoop</c>, <c>DeliveryDirector</c>,
///     <c>WordTargetEnforcer</c>, <c>ScriptGenerationEngine</c>).
/// </summary>
public sealed record ScriptPipelineRoles(
    IScriptGenerator ScriptGenerator,
    IScriptReviewer ScriptReviewer,
    IScriptRefiner ScriptRefiner,
    IScriptDirector ScriptDirector,
    IScriptSafetyReviewer ScriptSafetyReviewer,
    IContentSafetyFilter ContentSafetyFilter,
    IDurationPredictor? DurationPredictor = null,
    ISpokenDurationModel? SpokenDurationModel = null,
    // Capture sink for measured (shape -> natural seconds) samples, plus the shape coefficients the
    // sample is measured with. Optional so an orchestrator built by hand in a test needs no
    // telemetry wiring; when either is absent, capture is simply skipped.
    ISpokenDurationSampleRepository? DurationSamples = null,
    SpokenDurationCalibration? DurationCalibration = null);
