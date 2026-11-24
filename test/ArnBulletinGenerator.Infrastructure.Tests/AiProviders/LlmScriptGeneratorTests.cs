using System.Net;
using System.Linq;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Microsoft.SemanticKernel removed — no longer in project
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class LlmScriptGeneratorTests : IDisposable
{
    private readonly FakeHttpHandler _httpHandler;
    private readonly LlmScriptGenerator _sut;

    public LlmScriptGeneratorTests()
    {
        _httpHandler = new FakeHttpHandler();

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("AzureOpenAI").Returns(_ => new HttpClient(_httpHandler));


        var broadcastOptions = Options.Create(new BroadcastOptions { WordsPerMinute = 150, SyllablesPerSecond = 5.8 });
        var azureOptions = Options.Create(new AzureAIOptions
        {
            Endpoint = "https://test.openai.azure.com",
            Model = "o4-mini",
            ApiKey = "test-key"
        });

        _sut = new LlmScriptGenerator(
            httpClientFactory,
            broadcastOptions,
            azureOptions,
            TimeProvider.System,
            Substitute.For<ILogger<LlmScriptGenerator>>(),
            Substitute.For<ILlmCallCapture>());
    }

    public void Dispose()
    {
        _httpHandler.Dispose();
        GC.SuppressFinalize(this);
    }


    // ── ReviewScriptAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ReviewScriptAsync_Approved_ReturnsApprovedResult()
    {
        string reviewJson = JsonSerializer.Serialize(new { verdict = "APPROVED", issues = Array.Empty<string>() });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Some good script text.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeTrue();
        result.Value.Feedback.ShouldBe("Approved.");
    }
    [Fact]
    public async Task ReviewScriptAsync_SendsNoSlotBudgetAndNoAssembledScript()
    {
        // CONTRACT CHANGE, 2026-08-25. Review judges SENTENCES; nothing is assembled when it runs,
        // so there is no read to time and no budget to hold it to. Measured on Foundry traces the
        // same day: review raised 1 defect across 34 bulletins while 38 of 54 refine calls were
        // "extend this sentence toward the word target". Giving a judge a slot budget invites it to
        // reason about fit — which is the fitter's job, settled from measured spoken duration, a
        // number an LLM cannot estimate. Selection is free, so "too many sentences" is not a defect
        // and a reviewer trimming for length destroys options it cannot price.
        _httpHandler.SetResponse(ModelResponse("""{"verdict":"APPROVED","issues":[]}"""));

        var result = await _sut.ReviewScriptAsync("Some good script text.", 30);

        result.IsSuccess.ShouldBeTrue();
        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldNotContain("REVIEW TARGET");
        input.ShouldNotContain("30 seconds");
        input.ShouldNotContain("CURRENT SCRIPT");
        input.ShouldNotContain("Some good script text.");
    }

    [Fact]
    public async Task ReviewScriptAsync_SendsTheDraftedSentencesToJudge()
    {
        IReadOnlyList<DraftedSentence> draftedSentences =
            [new DraftedSentence(1, "The M1 is heavy.", ["corridor:M1#1:incident:0"])];
        _httpHandler.SetResponse(ModelResponse("""{"verdict":"APPROVED","issues":[]}"""));

        var result = await _sut.ReviewScriptAsync(
            "The M1 is heavy.",
            30,
            draftedSentences: draftedSentences);

        result.IsSuccess.ShouldBeTrue();
        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldContain("DRAFTED SENTENCES");
        input.ShouldContain("\"order\":1");
        input.ShouldContain("\"evidenceIds\":[\"corridor:M1#1:incident:0\"]");
        input.ShouldContain("\"text\":\"The M1 is heavy.\"");

        // `order` is a repair handle, not a running order — the fitter decides what airs where.
        input.ShouldContain("order is a repair handle, not position on air");
    }

    [Fact]
    public async Task ReviewScriptAsync_WithoutStructuredDraft_OmitsStructuredDraftContext()
    {
        _httpHandler.SetResponse(ModelResponse("""{"verdict":"APPROVED","issues":[]}"""));

        var result = await _sut.ReviewScriptAsync("The M1 is heavy.", 30);

        result.IsSuccess.ShouldBeTrue();
        DecodedInput(_httpHandler.LastRequestBody).ShouldNotContain("STRUCTURED DRAFT (judge");
    }

    [Fact]
    public async Task ReviewScriptAsync_WithEmptyStructuredDraft_OmitsStructuredDraftContext()
    {
        _httpHandler.SetResponse(ModelResponse("""{"verdict":"APPROVED","issues":[]}"""));

        var result = await _sut.ReviewScriptAsync(
            "The M1 is heavy.",
            30,
            draftedSentences: []);

        result.IsSuccess.ShouldBeTrue();
        DecodedInput(_httpHandler.LastRequestBody).ShouldNotContain("STRUCTURED DRAFT");
    }

    [Fact]
    public async Task ReviewScriptAsync_WithCitationlessStructuredDraft_OmitsStructuredDraftContext()
    {
        _httpHandler.SetResponse(ModelResponse("""{"verdict":"APPROVED","issues":[]}"""));

        var result = await _sut.ReviewScriptAsync(
            "The M1 is heavy.",
            30,
            draftedSentences: [new DraftedSentence(1, "The M1 is heavy.", [])]);

        result.IsSuccess.ShouldBeTrue();
        DecodedInput(_httpHandler.LastRequestBody).ShouldNotContain("STRUCTURED DRAFT");
    }


    [Fact]
    public async Task ReviewScriptAsync_NeedsRevision_ReturnsFeedback()
    {
        string reviewJson = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new[] { "Contains sign-off", "Too many numbers" }
        });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Bad script ending with take care out there.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Feedback.ShouldContain("sign-off");
    }

    [Fact]
    public async Task ReviewScriptAsync_ModelError_ReturnsFailure()
    {
        _httpHandler.SetResponse("Service Unavailable", HttpStatusCode.ServiceUnavailable);

        var result = await _sut.ReviewScriptAsync("Any script.", 30);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RewriteAsync_SendsWorkflowShapedReferenceDataAndWordLimit()
    {
        _httpHandler.SetResponse(ModelResponse("""{"script":"Lead with the M4."}"""));
        var incident = new TrafficIncident("i1", "At M4 - Congestion", 2, "M4", "congestion");
        var context = CreateContext(corridors:
        [
            new TrafficCorridor("M4", 2, 2, 1000, 20, "congestion", "Heavy congestion", [incident])
        ]) with
        {
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var budget = new RewriteWordBudget(57, 53, 61);
        var result = await _sut.RewriteAsync(
            "The M4 has heavy congestion.", "Lead with the M4.", context,
            wordBudget: budget);

        result.IsSuccess.ShouldBeTrue();
        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldContain("REFERENCE DATA:");
        input.ShouldContain("# M4");
        input.ShouldContain("road = M4");
        input.ShouldContain("CURRENT BULLETIN:");
        input.ShouldContain("The M4 has heavy congestion.");
        input.ShouldContain("WORD BUDGET:");
        input.ShouldContain("complete read is currently 5 words");
        input.ShouldContain("Aim for 57 words");
        input.ShouldContain("acceptable range of 53 to 61 words");
        input.ShouldContain("line 1: 5");
    }
    [Fact]
    public async Task DeliveryWriterAsync_SendsWordBudgetAndLineCounts()
    {
        _httpHandler.SetResponse(ModelResponse("""{"script":"The M4 is busy."}"""));
        var budget = new RewriteWordBudget(4, 4, 6);

        var result = await _sut.RewriteAsync(
            "The M4 is busy.", "make the delivery calmer", "eleven_v3",
            wordBudget: budget);

        result.IsSuccess.ShouldBeTrue();
        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldContain("WORD BUDGET");
        input.ShouldContain("Currently 4 words");
        input.ShouldContain("Aim for 4 words");
        input.ShouldContain("Acceptable range: 4 to 6 words");
        input.ShouldContain("line 1: 4");
    }

    [Fact]
    public async Task DeliveryWriterAsync_RejectsBelowMinimumBudget()
    {
        _httpHandler.SetResponseSequence(
            (ModelResponse("""{"script":"The M4 is busy."}"""), HttpStatusCode.OK),
            (ModelResponse("""{"script":"The M4 is busy."}"""), HttpStatusCode.OK),
            (ModelResponse("""{"script":"The M4 is busy."}"""), HttpStatusCode.OK));

        var result = await _sut.RewriteAsync(
            "The M4 is busy.", "make the delivery calmer", "eleven_v3",
            wordBudget: new RewriteWordBudget(6, 5, 7));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("complete-read range 5-7");
        _httpHandler.CallCount.ShouldBe(3);
    }

    // ── PatchSentenceAsync (Sentence-scoped Refine) ────────────────────────

    [Fact]
    public async Task PatchSentenceAsync_WellFormedWithinBudget_Accepted()
    {
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":["corridor:M1#1:impact"],"variants":[{"style":"standard","text":"Pacific Motorway inbound has minor delays."}]}"""));

        var originalSentence = new DraftedSentence(1, "Pacific Motorway has delays.", ["corridor:M1#1:impact"]);
        var allowed = new[] { new EvidenceUnit("corridor:M1#1:impact", "corridor:M1", "impact", "minor delays") };

        var result = await _sut.PatchSentenceAsync(
            CreateContext(), originalSentence, "Specify inbound direction", allowed, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Text.ShouldBe("Pacific Motorway inbound has minor delays.");
        result.Value.EvidenceIds.ShouldContain("corridor:M1#1:impact");
    }

    [Fact]
    public async Task PatchSentenceAsync_InflatesBeyondTolerance_Rejected()
    {
        // Original is 5 words, budget is 5 words. Response is 15 words -> severe inflation.
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":["corridor:M1#1:impact"],"variants":[{"style":"standard","text":"Pacific Motorway inbound has extremely heavy crawling delays stretching over ten kilometres past the exit."}]}"""));

        var originalSentence = new DraftedSentence(1, "Pacific Motorway has delays.", ["corridor:M1#1:impact"]);
        var allowed = new[] { new EvidenceUnit("corridor:M1#1:impact", "corridor:M1", "impact", "minor delays") };

        var result = await _sut.PatchSentenceAsync(
            CreateContext(), originalSentence, "Add extra context", allowed, 5);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("inflated beyond word tolerance");
    }

    [Fact]
    public async Task PatchSentenceAsync_CitesUnrecognisedEvidenceId_KeepsRepairAndCarriesScopeForward()
    {
        // CONTRACT CHANGE, 2026-08-25. This used to reject, and it fired on 41% of captured refine
        // calls (12 of 29): RenderEvidenceUnits prints a group heading above the bracketed ids, so
        // models cite the heading. The repair itself is sound — the caller already fixed the scope
        // by supplying `allowedEvidence` — so the citation is our own input echoed back and carries
        // nothing we do not already hold. Throwing the patch away kept the DEFECTIVE sentence on air.
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":["M1, Springwood"],"variants":[{"style":"standard","text":"The M1 has minor delays."}]}"""));

        var originalSentence = new DraftedSentence(1, "Pacific Motorway has delays.", ["corridor:M1#1:impact"]);
        var allowed = new[] { new EvidenceUnit("corridor:M1#1:impact", "corridor:M1", "impact", "minor delays") };

        var result = await _sut.PatchSentenceAsync(
            CreateContext(), originalSentence, "Fix road", allowed, 5);

        result.IsSuccess.ShouldBeTrue("an unrecognised citation must never discard a sound repair");
        result.Value.Text.ShouldBe("The M1 has minor delays.");
        result.Value.EvidenceIds.ShouldBe(["corridor:M1#1:impact"]);
    }

    [Fact]
    public async Task PatchSentenceAsync_CitationCanNeverWidenBeyondTheSuppliedScope()
    {
        // The guardrail the old reject was really protecting, kept intact: whatever the model cites,
        // the resolved evidence stays inside the set the caller permitted. A refine may re-word a
        // fact; it may never acquire a new one.
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":["corridor:M7#1:impact","corridor:M1#1:impact"],"variants":[{"style":"standard","text":"The M1 has minor delays."}]}"""));

        var originalSentence = new DraftedSentence(1, "Pacific Motorway has delays.", ["corridor:M1#1:impact"]);
        var allowed = new[] { new EvidenceUnit("corridor:M1#1:impact", "corridor:M1", "impact", "minor delays") };

        var result = await _sut.PatchSentenceAsync(
            CreateContext(), originalSentence, "Fix road", allowed, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EvidenceIds.ShouldBe(["corridor:M1#1:impact"], "the unsupplied M7 citation must not survive");
    }

    [Fact]
    public async Task PatchSentenceAsync_MultiSentenceReturn_Rejected()
    {
        // One sentence in, one sentence out: a two-sentence return fits the word budget here, so
        // only the single-sentence guard can catch it — without it the splice would grow the script
        // and shift every later sentence's order mid-loop.
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":["corridor:M1#1:impact"],"variants":[{"style":"standard","text":"Pacific Motorway inbound has minor delays. Allow extra time through Springwood."}]}"""));

        var originalSentence = new DraftedSentence(1, "Pacific Motorway has delays.", ["corridor:M1#1:impact"]);
        var allowed = new[] { new EvidenceUnit("corridor:M1#1:impact", "corridor:M1", "impact", "minor delays") };

        var result = await _sut.PatchSentenceAsync(
            CreateContext(), originalSentence, "Add extra context", allowed, 12);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("multiple sentences");
    }

    [Fact]
    public void RenderedRefineSentencePrompt_RepresentativeSentence_Under2KBAndMentionsJson()
    {
        var allowed = new[]
        {
            new EvidenceUnit("corridor:M1#1:impact", "corridor:Pacific Motorway", "impact", "minor delays"),
            new EvidenceUnit("corridor:M1#1:suburb", "corridor:Pacific Motorway", "suburb", "Springwood")
        };
        var arguments = new Dictionary<string, object?>
        {
            ["sentenceOrder"] = 2,
            ["sentenceText"] = "Pacific Motorway has delays near Springwood.",
            ["allowedEvidence"] = PromptLibrary.RenderEvidenceUnits(allowed),
            ["instruction"] = "Specify that delays are only minor inbound.",
            ["wordBudget"] = 7
        };

        string rendered = HandlebarsTemplateRenderer.Render(PromptLibrary.RefineSentenceTemplate, arguments);
        int sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(rendered);

        sizeInBytes.ShouldBeLessThan(2048);
        rendered.Contains("json", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }
    // ── RenderEvidenceUnits (source-complete evidence rendering) ─────────

    [Fact]
    public void RenderEvidenceUnits_ValueWithoutRoadName_ExposesRoadInTheHeading()
    {
        // FormatImpact intentionally returns a generic label ("moderate delays") that never names
        // the road, so the rendering must still make the road explicit — otherwise a reader seeing
        // only field+value is blind to which road the fact belongs to. Since 2026-08-20 the road is
        // carried ONCE by the group heading instead of being reprinted on every line as
        // "source=corridor <road> near <suburb>; field=", which cost ~60 chars per unit.
        var unit = new EvidenceUnit("corridor:Pacific Motorway#1:impact", "corridor:Pacific Motorway",
            "impact", "moderate delays");

        string rendered = PromptLibrary.RenderEvidenceUnits([unit]);

        rendered.ShouldContain("Pacific Motorway");
        rendered.ShouldContain("impact = moderate delays");
        rendered.ShouldNotContain("source=");
    }

    [Fact]
    public void RenderEvidenceUnits_CorridorSuburb_QualifiesTheHeadingOnce()
    {
        var units = new[]
        {
            new EvidenceUnit("corridor:Captain Cook Highway#2:delayMinutes",
                "corridor:Captain Cook Highway", "delayMinutes", "12"),
            new EvidenceUnit("corridor:Captain Cook Highway#2:suburb",
                "corridor:Captain Cook Highway", "suburb", "Cairns North")
        };

        string rendered = PromptLibrary.RenderEvidenceUnits(units);

        rendered.ShouldContain("Captain Cook Highway, Cairns North");

        // The id stays byte-exact: it remains the only citable handle, so the draft's evidenceIds
        // contract is unaffected by the rendering change.
        rendered.ShouldContain("[corridor:Captain Cook Highway#2:delayMinutes] delayMinutes = 12");
    }

    [Fact]
    public void RenderEvidenceUnits_HeadingIsMarkedSoItCannotBeMistakenForACitableId()
    {
        // 2026-08-25: the heading used to sit bare on its own line directly above the bracketed
        // ids, and models cited it verbatim ("M5, Kingsgrove") in 41% of captured refine calls.
        // Brackets mean citable; "# " means label. The distinction must be visible in the prompt.
        var unit = new EvidenceUnit("weather:condition", "weather", "condition", "partly cloudy");

        string rendered = PromptLibrary.RenderEvidenceUnits([unit]);

        rendered.ShouldStartWith("# weather");
        rendered.ShouldContain("[weather:condition] condition = partly cloudy");

        // The heading text itself is never wrapped in brackets - only real ids are.
        rendered.ShouldNotContain("[weather]");
    }

    [Fact]
    public async Task PatchSentenceAsync_PromptSurfacesRoadForImpactValueThatOmitsIt()
    {
        // End-to-end: BuildEvidenceUnits -> RenderEvidenceUnits -> refine.md's {{{evidenceUnits}}}
        // placeholder. Proves the wiring keeps the road visible in the rendered RefineSentence
        // prompt text sent to the model, not only inside the opaque schema-enum id.
        //
        // This used to assert visibility through the road-feel wording, which never names the road.
        // Two things changed. The road is now its own evidence fact, so visibility no longer rests
        // on a severity phrase happening to mention it. And this corridor has a measured delay
        // (25 minutes against a typical 10), so it gets no road-feel at all: a measurement outranks
        // our derivation of it, and shipping both handed the writer two severity scales that
        // disagree on 7 of 9 delay values.
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":[],"variants":[{"style":"standard","text":"Pacific Motorway has delays."}]}"""));
        var incident = new TrafficIncident(
            "i3", "At Chapel Street - Lane closure due to works", 2, "Pacific Motorway", "laneRestriction");
        var corridor = new TrafficCorridor(
            "Pacific Motorway", 2, 3, 2000, 60, "congestion",
            "Slow moving traffic through the interchange", [incident],
            Extent: Option<string>.Some("City Road to Toorak Road"),
            DriveTimeMinutes: Option<int>.Some(25),
            NormalDriveTimeMinutes: Option<int>.Some(10));
        var context = CreateContext(corridors: [corridor]);
        var sentence = new DraftedSentence(1, "Pacific Motorway has delays.", []);

        await _sut.PatchSentenceAsync(context, sentence, "Add more detail", [], 20);

        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldContain("road = Pacific Motorway");
        // A measured delay suppresses our derived band entirely.
        input.ShouldNotContain("road feel");
        // The band itself must never reach the model - that is the whole point of speaking it first.
        input.ShouldNotContain("moderate delays");
        input.ShouldContain("Pacific Motorway");
    }


    // ── ParseReviewResult via ReviewScriptAsync ────────────────────────

    [Fact]
    public async Task ReviewScriptAsync_MalformedJson_ReturnsFalseWithRawResponse()
    {
        _httpHandler.SetResponse(ModelResponse("this is not valid json at all"));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Feedback.ShouldContain("this is not valid json at all");
    }

    [Fact]
    public async Task ReviewScriptAsync_MalformedJsonContainingApproved_ReturnsApproved()
    {
        _httpHandler.SetResponse(ModelResponse("The script is Approved and good to go."));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeTrue();
        result.Value.Feedback.ShouldBe("Approved.");
    }

    [Fact]
    public async Task ReviewScriptAsync_MalformedJsonContainingNotApproved_ReturnsFalse()
    {
        _httpHandler.SetResponse(ModelResponse("Script not approved, needs revision"));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
    }

    [Fact]
    public async Task ReviewScriptAsync_JsonMissingVerdictField_ReturnsFalse()
    {
        string json = JsonSerializer.Serialize(new { foo = "bar" });
        _httpHandler.SetResponse(ModelResponse(json));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
    }

    [Fact]
    public async Task ReviewScriptAsync_VerdictApproveVariant_ReturnsApproved()
    {
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { verdict = "APPROVE" })));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewScriptAsync_VerdictPassVariant_ReturnsApproved()
    {
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { verdict = "PASS" })));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewScriptAsync_VerdictAcceptVariant_ReturnsApproved()
    {
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { verdict = "ACCEPT" })));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewScriptAsync_StructuredIssues_ParsesSentenceAndScriptScopedIssues()
    {
        string reviewJson = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new object[]
            {
                new { order = 1, scope = "Sentence", defect = "WRONG_INCIDENT_TYPE", reason = "M1 called crash instead of breakdown" },
                new { scope = "Script", defect = "MISSING_REQUIRED_SECTION", reason = "Fuel report missing entirely" }
            }
        });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Script with two issues.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Issues.Count.ShouldBe(2);

        result.Value.Issues[0].Order.ShouldBe(1);
        result.Value.Issues[0].Scope.ShouldBe(RefineScope.Sentence);
        result.Value.Issues[0].Defect.ShouldBe("WRONG_INCIDENT_TYPE");
        result.Value.Issues[0].Reason.ShouldBe("M1 called crash instead of breakdown");
        result.Value.Issues[0].IsPatchable.ShouldBeTrue();

        result.Value.Issues[1].Order.ShouldBeNull();
        result.Value.Issues[1].Scope.ShouldBe(RefineScope.Script);
        result.Value.Issues[1].Defect.ShouldBe("MISSING_REQUIRED_SECTION");
        result.Value.Issues[1].Reason.ShouldBe("Fuel report missing entirely");
        result.Value.Issues[1].IsPatchable.ShouldBeFalse();

        result.Value.Feedback.ShouldContain("M1 called crash instead of breakdown");
        result.Value.Feedback.ShouldContain("Fuel report missing entirely");
    }

    [Fact]
    public async Task ReviewScriptAsync_LegacyProseArray_ParsesAsUnresolvedSentenceScopeWithoutThrowing()
    {
        string reviewJson = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new[] { "Mulgrave Road closure not mentioned", "Weather missing" }
        });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Legacy script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Issues.Count.ShouldBe(2);

        result.Value.Issues[0].Order.ShouldBeNull();
        result.Value.Issues[0].Scope.ShouldBe(RefineScope.Sentence);
        result.Value.Issues[0].IsPatchable.ShouldBeFalse();
        result.Value.Issues[0].Reason.ShouldBe("Mulgrave Road closure not mentioned");

        result.Value.Issues[1].Order.ShouldBeNull();
        result.Value.Issues[1].Scope.ShouldBe(RefineScope.Sentence);
        result.Value.Issues[1].IsPatchable.ShouldBeFalse();
        result.Value.Issues[1].Reason.ShouldBe("Weather missing");

        result.Value.Feedback.ShouldBe("Mulgrave Road closure not mentioned Weather missing");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData("not-a-number")]
    public async Task ReviewScriptAsync_MalformedOrder_YieldsUnresolvedIssue(object? invalidOrder)
    {
        var issueObj = new Dictionary<string, object?>
        {
            ["defect"] = "FABRICATION",
            ["reason"] = "Unnamed back street mentioned",
            ["scope"] = "Sentence"
        };
        if (invalidOrder is not null)
            issueObj["order"] = invalidOrder;

        string reviewJson = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new[] { issueObj }
        });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Script with malformed order.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Issues.Count.ShouldBe(1);
        result.Value.Issues[0].Order.ShouldBeNull();
        result.Value.Issues[0].Scope.ShouldBe(RefineScope.Sentence);
        result.Value.Issues[0].IsPatchable.ShouldBeFalse();
        result.Value.Issues[0].Defect.ShouldBe("FABRICATION");
        result.Value.Issues[0].Reason.ShouldBe("Unnamed back street mentioned");
        result.Value.Feedback.ShouldBe("Unnamed back street mentioned");
    }

    [Fact]
    public async Task ReviewScriptAsync_Feedback_ReproducesHumanReadableTextDerivedFromIssues()
    {
        string reviewJson = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new object[]
            {
                new { order = 2, scope = "Sentence", defect = "CONTRADICTED_CONDITION", reason = "M4 is moderate delay, not severe" },
                new { order = 3, scope = "Sentence", defect = "NOT_FROM_DATA", reason = "Take care out there is commentary" }
            }
        });
        _httpHandler.SetResponse(ModelResponse(reviewJson));

        var result = await _sut.ReviewScriptAsync("Script for feedback reproduction test.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Feedback.ShouldBe("M4 is moderate delay, not severe Take care out there is commentary");
    }

    [Fact]
    public async Task ReviewScriptAsync_JsonWithMultipleFeedbackSources_CollectsAll()
    {
        string json = JsonSerializer.Serialize(new
        {
            verdict = "NEEDS_REVISION",
            issues = new[] { "Issue one" },
            reason = "Too formal",
            feedback = "Needs more slang"
        });
        _httpHandler.SetResponse(ModelResponse(json));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
        result.Value.Feedback.ShouldContain("Issue one");
        result.Value.Feedback.ShouldContain("Too formal");
        result.Value.Feedback.ShouldContain("Needs more slang");
    }

    [Fact]
    public async Task ReviewScriptAsync_JsonArrayResponse_ReturnsFalse()
    {
        _httpHandler.SetResponse(ModelResponse("[1, 2, 3]"));

        var result = await _sut.ReviewScriptAsync("Some script.", 30);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsApproved.ShouldBeFalse();
    }


    [Theory]
    [InlineData("gpt-5", false, "minimal")]
    [InlineData("gpt-5-mini", false, "minimal")]
    [InlineData("gpt-5-nano", false, "minimal")]
    [InlineData("gpt-5.1", false, "none")]
    [InlineData("gpt-5.4-mini", false, "none")]
    [InlineData("gpt-5.4-nano", false, "none")]
    [InlineData("o4-mini", false, "low")]
    [InlineData("gpt-5-mini", true, "low")]
    [InlineData("gpt-5.1", true, "low")]
    public void ResolveReasoningEffort_PicksValidTierPerModel(string model, bool feedback, string expected)
    {
        ResponsesTransport.ResolveReasoningEffort(model, feedback).ShouldBe(expected);
    }

    [Fact]
    public async Task ReviewScriptAsync_RequestPayload_UsesJsonFormat()
    {
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { verdict = "APPROVED" })));

        await _sut.ReviewScriptAsync("Script text.", 30);

        using var doc = JsonDocument.Parse(_httpHandler.LastRequestBody);
        doc.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString()
            .ShouldBe("json_object");
    }


    [Fact]
    public async Task PatchSentenceAsync_FillerStatements_NotIncludedInPatchPrompt()
    {
        _httpHandler.SetResponse(ModelResponse(
            """{"order":1,"evidenceIds":[],"variants":[{"style":"standard","text":"Pacific Motorway has delays."}]}"""));
        string fillerText = "SENTINEL FILLER: Check your mirrors before changing lanes.";
        var context = CreateContext() with
        {
            FillerStatements = [new FillerStatement("f2", fillerText)]
        };
        var sentence = new DraftedSentence(1, "Pacific Motorway has delays.", []);

        await _sut.PatchSentenceAsync(context, sentence, "Make it longer", [], 20);

        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldNotContain(fillerText);
    }

    [Fact]
    public async Task ReviewScriptAsync_FillerStatements_NotIncludedInReviewPrompt()
    {
        string reviewJson = JsonSerializer.Serialize(new { verdict = "APPROVED", issues = Array.Empty<string>() });
        _httpHandler.SetResponse(ModelResponse(reviewJson));
        string fillerText = "SENTINEL FILLER: Keep your headlights on in poor visibility.";
        var context = CreateContext() with
        {
            FillerStatements = [new FillerStatement("f3", fillerText)]
        };

        await _sut.ReviewScriptAsync("Some good script text.", 30, context: context);

        string input = DecodedInput(_httpHandler.LastRequestBody);
        input.ShouldNotContain(fillerText);
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    /// <summary>The decoded draft "input" the model receives (the raw body JSON-escapes '&lt;' to \u003C).</summary>
    private static string DecodedInput(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static BroadcastWithTraffic CreateContext(
        string traffic = "Heavy congestion on M4",
        string location = "Sydney",
        Option<string> rejectionFeedback = default,
        IReadOnlyList<TrafficCorridor>? corridors = null)
    {
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "job-1",
            Location = location,
            Traffic = traffic,
            Incidents = corridors?.SelectMany(c => c.Incidents).ToList() ?? [],
            Corridors = corridors ?? [],
            RejectionFeedback = rejectionFeedback,
        };
    }

    [Fact]
    public async Task ReviewScriptAsync_AadTokenRejected_FallsBackToApiKey()
    {
        // The SUT authenticates with an AAD bearer token, but the resource rejects it (401) —
        // as in local dev when the signed-in principal lacks the OpenAI data role. A valid
        // api-key is configured, so the call must retry with the key and succeed.
        var sut = CreateSutWithAadToken();
        string reviewJson = JsonSerializer.Serialize(new { verdict = "APPROVED", issues = Array.Empty<string>() });
        _httpHandler.SetResponseSequence(
            ("Unauthorized: principal lacks OpenAI data role", HttpStatusCode.Unauthorized),
            (ModelResponse(reviewJson), HttpStatusCode.OK));

        var result = await sut.ReviewScriptAsync("Some good script text.", 30);

        result.IsSuccess.ShouldBeTrue($"401 on the AAD token should fall back to the api-key; got: {result.Error}");
        _httpHandler.CallCount.ShouldBe(2, "expected one rejected AAD call, then one api-key retry");
    }

    private LlmScriptGenerator CreateSutWithAadToken()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("AzureOpenAI").Returns(_ => new HttpClient(_httpHandler));
        var broadcastOptions = Options.Create(new BroadcastOptions { WordsPerMinute = 150, SyllablesPerSecond = 5.8 });
        var azureOptions = Options.Create(new AzureAIOptions
        {
            Endpoint = "https://test.openai.azure.com",
            Model = "o4-mini",
            ApiKey = "test-key"
        });

        return new LlmScriptGenerator(
            httpClientFactory, broadcastOptions, azureOptions,
            TimeProvider.System, Substitute.For<ILogger<LlmScriptGenerator>>(),
            Substitute.For<ILlmCallCapture>(), new StubTokenCredential());
    }

    // ── DirectAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DirectAsync_MissingTag_AddsConfiguredDefaultWithoutRetry()
    {
        const string plain = "Traffic is moving nicely across the M4 this morning.";
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { script = plain })));

        var result = await _sut.DirectAsync(
            plain,
            new DeliveryStylePolicy("Bright", ["[warmly]"], 2),
            operatorIntent: null,
            ttsModel: "eleven_v3");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("[warmly] " + plain);
        DeliveryTags.Strip(result.Value).ShouldBe(plain);
        _httpHandler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task DirectAsync_GraveRead_DoesNotAddEnergeticDefaultTag()
    {
        const string plain = "A serious injury has closed the road near the M4.";
        _httpHandler.SetResponse(ModelResponse(JsonSerializer.Serialize(new { script = plain })));

        var result = await _sut.DirectAsync(
            plain,
            new DeliveryStylePolicy("Bright", ["[brightly]"], 2),
            operatorIntent: null,
            ttsModel: "eleven_v3");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(plain);
        DeliveryTags.Extract(result.Value).ShouldBeEmpty();
        _httpHandler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task DirectAsync_ChangedWords_RetriesOnceThenFailsOpen()
    {
        const string plain = "Traffic is moving nicely across the M4 this morning.";
        string changed = JsonSerializer.Serialize(new { script = "Traffic is moving quickly across the M4 this morning." });
        _httpHandler.SetResponseSequence(
            (ModelResponse(changed), HttpStatusCode.OK),
            (ModelResponse(changed), HttpStatusCode.OK));

        var result = await _sut.DirectAsync(
            plain,
            new DeliveryStylePolicy("Bright", ["[warmly]"], 2),
            operatorIntent: null,
            ttsModel: "eleven_v3");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(plain);
        _httpHandler.CallCount.ShouldBe(2);
    }

    // ── ReviewScriptSafetyAsync ─────────────────────────────────────────

    [Fact]
    public async Task ReviewScriptSafetyAsync_SafeScript_ReturnsSafe()
    {
        _httpHandler.SetResponse(ModelResponse("""{"safe": true, "violations": []}"""));

        var result = await _sut.ReviewScriptSafetyAsync("Traffic is heavy on the M4 citybound.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSafe.ShouldBeTrue();
        result.Value.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReviewScriptSafetyAsync_Violation_ReturnsBlockedWithDetails()
    {
        _httpHandler.SetResponse(ModelResponse(
            """{"safe": false, "violations": [{"category": "self-harm", "text": "a man jumped from the bridge", "reason": "Mindframe: must be described as a police operation"}]}"""));

        var result = await _sut.ReviewScriptSafetyAsync("A man jumped from the bridge causing delays.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSafe.ShouldBeFalse();
        result.Value.Violations.Count.ShouldBe(1);
        result.Value.Violations[0].ShouldContain("self-harm");
        result.Value.Violations[0].ShouldContain("police operation");
        result.Value.Violations[0].ShouldContain("jumped from the bridge");
    }

    [Fact]
    public async Task ReviewScriptSafetyAsync_BlockedWithoutViolationList_StillReportsBlocked()
    {
        _httpHandler.SetResponse(ModelResponse("""{"safe": false}"""));

        var result = await _sut.ReviewScriptSafetyAsync("Some script.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSafe.ShouldBeFalse();
        result.Value.Violations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReviewScriptSafetyAsync_MalformedJson_ReturnsFailure()
    {
        _httpHandler.SetResponse(ModelResponse("The script looks fine to me!"));

        var result = await _sut.ReviewScriptSafetyAsync("Some script.");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not valid JSON");
    }

    [Fact]
    public async Task ReviewScriptSafetyAsync_MissingSafeProperty_ReturnsFailure()
    {
        _httpHandler.SetResponse(ModelResponse("""{"verdict": "ok"}"""));

        var result = await _sut.ReviewScriptSafetyAsync("Some script.");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("missing boolean 'safe'");
    }

    [Fact]
    public async Task ReviewScriptSafetyAsync_ModelError_ReturnsFailure()
    {
        _httpHandler.SetResponse("Service Unavailable", HttpStatusCode.ServiceUnavailable);

        var result = await _sut.ReviewScriptSafetyAsync("Some script.");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("503");
    }


    private sealed class StubTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("stub-aad-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("stub-aad-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static string ModelResponse(string text)
    {
        return JsonSerializer.Serialize(new
        {
            id = "resp_test",
            @object = "response",
            created_at = 1700000000,
            status = "completed",
            model = "gpt-5.4-mini",
            output = new[]
            {
                new
                {
                    type = "message",
                    id = "msg_1",
                    status = "completed",
                    role = "assistant",
                    content = new[]
                    {
                        new { type = "output_text", text, annotations = Array.Empty<object>() }
                    }
                }
            }
        });
    }


    /// <summary>
    ///     Captures HTTP requests and returns preconfigured responses.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(string Body, HttpStatusCode Status)> _responses = new();
        private string _singleBody = "";
        private HttpStatusCode _singleStatus = HttpStatusCode.OK;
        private bool _useQueue;

        public int CallCount { get; private set; }
        public string LastRequestBody { get; private set; } = "";

        public void SetResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _singleBody = body;
            _singleStatus = status;
            _useQueue = false;
        }

        public void SetResponseSequence(params (string Body, HttpStatusCode Status)[] responses)
        {
            _responses.Clear();
            foreach (var r in responses) _responses.Enqueue(r);
            _useQueue = true;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            (string body, var status) = _useQueue && _responses.Count > 0
                ? _responses.Dequeue()
                : (_singleBody, _singleStatus);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

}
