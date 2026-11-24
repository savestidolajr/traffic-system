using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     The deterministic floor under the reply format. Measured live 2026-08-26: a station
///     playbook block instructing "reply in JSON" beat the locked reply-format rule, every
///     grounding check passed (the facts inside the JSON were real), the editor approved it, and
///     a <c>{"script": …}</c> blob shipped as a finished read. Format is a machine property, so a
///     machine refuses it.
/// </summary>
public sealed class PlainSpokenProseCheckTests
{
    private static NormalizedInput Input()
    {
        var request = new WorkflowRequest(
            "job", "sydney", "KIIS 90s", "Sydney", 30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        return Normalizer.Normalize(request, NewTrace());
    }

    private static IReadOnlyList<CheckResult> Run(string read) =>
        ConversationalChecks.Run(Input(), read, BulletinBriefs.Empty, NewTrace());

    [Fact]
    public void A_plain_read_passes()
    {
        Run("South Road is slow through the works.")
            .Single(check => check.Name == "PlainSpokenProse")
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_json_wrapped_read_fails()
    {
        var result = Run("""{"script":"South Road is slow.","mood":"relaxed"}""")
            .Single(check => check.Name == "PlainSpokenProse");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("JSON");
    }

    [Fact]
    public void A_fenced_read_fails()
    {
        Run("```\nSouth Road is slow.\n```")
            .Single(check => check.Name == "PlainSpokenProse")
            .Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_markdown_heading_fails()
    {
        Run("# Traffic\nSouth Road is slow.")
            .Single(check => check.Name == "PlainSpokenProse")
            .Passed.ShouldBeFalse();
    }

    private static TraceRecorder NewTrace() =>
        new("run", "wf", "desc", "job", "market", "station", 30.0);
}
