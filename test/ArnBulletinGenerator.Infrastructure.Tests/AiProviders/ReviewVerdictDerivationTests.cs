using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

/// <summary>
///     The review verdict is derived from the issues, not taken on the model's word.
///     <para>
///         The reviewer runs at <c>reasoning=none</c>, and its schema used to require the verdict and
///         the defect BEFORE the reason. A model emits structured fields in schema order, so it had to
///         commit to "NEEDS_REVISION" and pick a defect code before analysing anything, leaving the
///         <c>reason</c> string as the only place to think. It duly thought there — and sometimes
///         thought its way back out again.
///     </para>
///     <para>
///         The first case below is verbatim from dev adelaide on 2026-08-26. The reviewer withdrew its
///         own finding mid-sentence and still returned NEEDS_REVISION, which cost a refine call and a
///         re-review to undo a defect that was never there.
///     </para>
/// </summary>
public sealed class ReviewVerdictDerivationTests
{
    [Fact]
    public void A_rejection_that_withdraws_its_own_finding_is_not_a_rejection()
    {
        const string response = """
            {
              "verdict": "NEEDS_REVISION",
              "issues": [
                {
                  "order": 2,
                  "scope": "sentence",
                  "defect": "WRONG_LOCATION",
                  "reason": "The supplied r8 record is for South Road in Richmond at James Congdon Drive, but it specifies only a road closure and does not support the sentence's wording that the closure is at James Congdon Drive? Actually the location is supported; no contradiction exists."
                }
              ]
            }
            """;

        var result = ModelResponseParser.ParseReviewResult(response);

        // The issue is still carried, because the model DID point at a sentence — the loop decides
        // what to do with it. What must not happen is the pipeline treating a withdrawn finding as
        // settled fact without the issue being inspectable.
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Reason.ShouldContain("no contradiction exists");
    }

    [Fact]
    public void NEEDS_REVISION_with_no_issue_to_point_at_is_an_approval()
    {
        // A rejection has to point at something. Without an issue there is nothing for refine to
        // act on, so re-drafting burns a round trip and lands somewhere equally unjudged.
        var result = ModelResponseParser.ParseReviewResult("""{"issues":[],"verdict":"NEEDS_REVISION"}""");

        result.IsApproved.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_issue_list_approves_however_the_verdict_is_spelled()
    {
        foreach (string verdict in new[] { "APPROVED", "approve", "PASS", "accept" })
            ModelResponseParser.ParseReviewResult($$"""{"issues":[],"verdict":"{{verdict}}"}""")
                .IsApproved.ShouldBeTrue(verdict);
    }

    [Fact]
    public void A_real_defect_still_rejects()
    {
        const string response = """
            {
              "issues": [
                {
                  "order": 1,
                  "scope": "sentence",
                  "reason": "The evidence names only the M8 as clear; the sentence claims other major roads are clear too.",
                  "defect": "UNGROUNDED_CLAIM"
                }
              ],
              "verdict": "NEEDS_REVISION"
            }
            """;

        var result = ModelResponseParser.ParseReviewResult(response);

        result.IsApproved.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Defect.ShouldBe("UNGROUNDED_CLAIM");
    }

    [Fact]
    public void A_response_carrying_neither_a_verdict_nor_an_issue_is_not_a_pass()
    {
        // Silence is not approval: an empty envelope means the check did not happen.
        ModelResponseParser.ParseReviewResult("{}").IsApproved.ShouldBeFalse();
    }
}
