using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Prompts;

/// <summary>
///     The instructions a station actually gets: our shipped default with their edits applied.
///     <para>
///         Shared by every stage that reads a block-marked prompt. It used to live private inside
///         <c>BulletinComposer</c>, which meant the composer was the ONLY stage a station could
///         edit — and once the desks took over qualitative voicing, the stage that decides how a
///         closure is described was the one stage ARN could not reach.
///     </para>
///     <para>
///         Refused edits are recorded on the trace rather than dropped. A station that rewrites a
///         block id we later renamed must be able to see that their change stopped taking effect,
///         and the trace is where anyone diagnosing a read is already looking.
///     </para>
/// </summary>
public static class StationPlaybook
{
    public static string Resolve(WorkflowRequest request, string role, TraceRecorder trace, string stage)
    {
        Playbook shipped = PlaybookParser.Parse(PromptLibrary.System(role));
        // The station's whole overlay narrowed to THIS role. One playbook configures every agent,
        // so the role qualifier travels inside the keys — see PlaybookOverlay.ForRole.
        PlaybookOverlay overlay = request.EffectivePolicy.PlaybookFor(role).ForRole(role);
        if (overlay.IsEmpty)
            return shipped.Render();

        PlaybookApplyResult result = shipped.Apply(overlay);
        trace.Decision(stage,
            $"Station playbook applied to {role}: {overlay.Rewrite.Count} rewritten, "
            + $"{overlay.Disable.Count} disabled, {overlay.Add.Count} added, {result.Refused.Count} refused",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = role,
                ["refused"] = result.Refused.Count == 0 ? "none" : string.Join(" | ", result.Refused)
            });

        return result.Playbook.Render();
    }
}
