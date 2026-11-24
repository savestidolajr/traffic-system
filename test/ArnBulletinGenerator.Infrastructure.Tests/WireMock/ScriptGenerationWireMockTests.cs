using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     WireMock integration tests for the Azure OpenAI script generation pipeline.
///     Exercises <c>IScriptGenerator</c>, <c>IScriptReviewer</c>, and <c>IScriptRefiner</c>
///     through the real HTTP client with a stubbed Azure OpenAI Responses API.
///     Response bodies are the real Responses API envelope (<c>output</c> array of message
///     items) — the SDK-based production code (<c>OpenAI.Responses.ResponsesClient</c> via
///     <c>ModelReaderWriter.Read&lt;ResponseResult&gt;</c>) does not recognise the old
///     shorthand <c>{"status":..., "output_text": "..."}</c> test fixture shape.
/// </summary>
[Trait("Category", "Slow")]
public sealed class ScriptGenerationWireMockTests : WireMockTestBase
{
    private const string AiEndpoint = "/openai/v1/responses";

    private static string OutputTextEnvelope(string text, string status = "completed") => /* language=json */ $$"""
    {
      "id": "resp_test",
      "object": "response",
      "created_at": 1700000000,
      "status": "{{status}}",
      "model": "test-model",
      "output": [
        {
          "type": "message",
          "id": "msg_1",
          "status": "completed",
          "role": "assistant",
          "content": [ { "type": "output_text", "text": {{JsonSerializer.Serialize(text)}}, "annotations": [] } ]
        }
      ]
    }
    """;

    /// <summary>
    ///     Wraps <paramref name="prose"/> as the single-sentence structured Draft payload. The
    ///     fixture cites the real M1 magnitude evidence unit, so the citation verifier does not
    ///     schedule a correction retry while these tests exercise transport behavior.
    /// </summary>
    private static string StructuredDraftText(string prose) => JsonSerializer.Serialize(new
    {
        sentences = new[] { new { order = 1, evidenceIds = new[] { "corridor:M1#1:magnitudeTier" }, text = prose } }
    });

    private static readonly string DraftResponse = OutputTextEnvelope(StructuredDraftText(
        "Morning, Sydney. M1 inbound is heavy from Huntingdale Road to Warrigal Road, so allow extra time. Meanwhile over on the West Gate Freeway, outbound traffic is crawling from Montague Street to Bolte Bridge. CityLink outbound is also copping it at Brunswick. Tullamarine Freeway inbound backed up from Bell Street to the airport. Mornington Peninsula Freeway northbound slow at Dromana. EastLink inbound heavy at Ringwood. Overall traffic is building right across the network."));

    private static readonly string ReviewApprovedResponse = OutputTextEnvelope(
        """{"verdict": "APPROVED", "issues": [], "reasoning": ["Script meets all requirements"]}""");

    private static readonly string ReviewRejectedResponse = OutputTextEnvelope(
        """{"verdict": "REJECT", "issues": ["Script is too short for 30 second target", "Missing start location for M1", "Add drive time for Tullamarine"], "reasoning": ["Duration 18.5s is well below 30s target"]}""");

    private static readonly string PatchResponse = OutputTextEnvelope(
        """{"order":1,"evidenceIds":[],"variants":[{"style":"standard","text":"Morning Sydney, the M1 inbound is heavy from Huntingdale Road to Warrigal Road."}]}""");


    private IScriptReviewer _reviewer = null!;
    private IScriptRefiner _refiner = null!;
    private BroadcastWithTraffic _testData = null!;

    protected override Task OnInitializedAsync()
    {
        // Resolve services — DI is available here (after InitializeAsync runs)
        _reviewer = Services.GetRequiredService<IScriptReviewer>();
        _refiner = Services.GetRequiredService<IScriptRefiner>();

        // Stub: default draft generation
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(AiEndpoint))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(DraftResponse));

        var corridors = new List<TrafficCorridor>
        {
            new("M1", 1, 3, 1000, 50.0, "crash",
                "Multi-vehicle crash at Springvale Road",
                [new TrafficIncident("i1", "Multi-vehicle crash blocking two lanes", 1, "M1 at Springvale", "crash", 1000)]),
            new("West Gate Freeway", 1, 2, 2000, 60.0, "congestion", "Heavy traffic from Montague Street",
                [new TrafficIncident("i2", "Outbound slow from Montague St", 1, "West Gate Freeway", "congestion", 2000)]),
            new("Tullamarine Freeway", 1, 2, 1500, 40.0, "congestion", "Inbound slow from Bell Street",
                [new TrafficIncident("i3", "Inbound backed up from Bell St", 1, "Tullamarine Freeway", "congestion", 1500)])
        };

        _testData = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30.0,
            JobId = "test-job-001",
            Location = "Melbourne",
            Traffic = "Traffic update: M1 inbound at Springvale, West Gate Freeway outbound slow, Tullamarine Freeway inbound slow.",
            Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = corridors,
            City = Option<string>.Some("Melbourne")
        };

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReviewScriptAsync_ApprovesScript()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(AiEndpoint))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ReviewApprovedResponse));

        var result = await _reviewer.ReviewScriptAsync(
            "Morning Sydney, M1 inbound is heavy.",
            30, null, null, null, CancellationToken.None, null);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsApproved);
    }

    [Fact]
    public async Task ReviewScriptAsync_ReturnsFeedback_WhenRejected()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(AiEndpoint))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ReviewRejectedResponse));

        var result = await _reviewer.ReviewScriptAsync(
            "Traffic is bad.", 30, null, null, null, CancellationToken.None, null);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.False(result.Value.IsApproved, "Should not be approved");
    }

    [Fact]
    public async Task PatchSentenceAsync_ReturnsPatchedSentence()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher(AiEndpoint))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(PatchResponse));

        var sentence = new DraftedSentence(1, "Morning Sydney, M1 is heavy.", []);
        var result = await _refiner.PatchSentenceAsync(
            _testData, sentence, "Add more detail about the M1", [], 20,
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!.Text);
    }

    // ── Contract Verification Tests ────────────────────────────────────
    // These validate that our outgoing request JSON and expected response JSON
    // match the actual API contract. No real API key needed — they verify
    // that our code constructs compatible messages.

    [Fact]
    public void DraftResponse_HasAllParserRequiredFields()
    {
        // This validates that the response JSON we return in WireMock stubs actually contains
        // all the fields OpenAI.Responses.ResponseResult (via ModelReaderWriter) requires.
        using var doc = JsonDocument.Parse(DraftResponse);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var status));
        Assert.Equal("completed", status.GetString());

        // ResponseResult.GetOutputText() reads the first message item's output_text content part.
        Assert.True(root.TryGetProperty("output", out var output));
        Assert.Equal(JsonValueKind.Array, output.ValueKind);
        Assert.NotEmpty(output.EnumerateArray());

        var message = output[0];
        Assert.Equal("message", message.GetProperty("type").GetString());
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        var textPart = content[0];
        Assert.Equal("output_text", textPart.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(textPart.GetProperty("text").GetString()),
            "output[].content[].text should not be empty");
    }

    [Fact]
    public void DraftResponse_OutputArrayHasWellFormedContentItems()
    {
        // Every content item in the output array must carry a string 'type' the SDK can
        // discriminate on (output_text, refusal, etc.).
        using var doc = JsonDocument.Parse(DraftResponse);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array);
        foreach (var item in output.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array);
            foreach (var contentItem in content.EnumerateArray())
            {
                Assert.True(contentItem.TryGetProperty("type", out var type));
                Assert.Equal(JsonValueKind.String, type.ValueKind);
            }
        }
    }

    private static string ExtractOutputText(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        return doc.RootElement.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()
               ?? throw new InvalidOperationException("output[0].content[0].text missing");
    }

    [Fact]
    public void ReviewResponse_HasVerdictField()
    {
        // ParseReviewResult reads the model's output text as JSON and looks for 'verdict'.
        string outputText = ExtractOutputText(ReviewApprovedResponse);

        using var innerDoc = JsonDocument.Parse(outputText);
        var innerRoot = innerDoc.RootElement;

        Assert.True(innerRoot.TryGetProperty("verdict", out var verdict), "Missing 'verdict' in review JSON");
        Assert.True(verdict.ValueKind == JsonValueKind.String, "'verdict' should be a string");

        // Valid verdict values the parser checks: APPROVED, APPROVE, ACCEPT, PASS
        var verdictStr = verdict.GetString() ?? "";
        Assert.Contains(verdictStr, new[] { "APPROVED", "APPROVE", "ACCEPT", "PASS", "REJECT" });
    }

    [Fact]
    public void ReviewRejectedResponse_HasIssuesArray()
    {
        string outputText = ExtractOutputText(ReviewRejectedResponse);

        using var innerDoc = JsonDocument.Parse(outputText);
        var innerRoot = innerDoc.RootElement;

        // ParseReviewResult reads 'issues', 'reasoning', 'reason', 'message', 'feedback'
        Assert.True(innerRoot.TryGetProperty("issues", out var issues),
            "Missing 'issues' in rejected review");
        Assert.True(issues.ValueKind == JsonValueKind.Array, "'issues' should be an array");
        Assert.NotEmpty(issues.EnumerateArray());

        Assert.True(innerRoot.TryGetProperty("reasoning", out var reasoning),
            "Missing 'reasoning' in rejected review");
    }

}
