using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

/// <summary>
///     The parser feeds prompt-cache observability, so the contract that matters is: read the real
///     Responses API shape correctly, and never throw on anything else. A capture that blows up on
///     an error body would take a bulletin down for the sake of a log line.
/// </summary>
public class TokenUsageParserTests
{
    // Shape taken verbatim from a captured dev call (job-diagnostics/*/llm_calls/*.json).
    private const string RealResponseBody = """
        {
          "id": "resp_abc", "object": "response", "status": "completed",
          "model": "gpt-5.6-luna",
          "output": [{"type": "message", "role": "assistant",
            "content": [{"type": "output_text", "text": "OK", "annotations": []}]}],
          "usage": {
            "input_tokens": 4574,
            "input_tokens_details": {"cached_tokens": 3840},
            "output_tokens": 142,
            "output_tokens_details": {"reasoning_tokens": 0},
            "total_tokens": 4716
          }
        }
        """;

    [Fact]
    public void TryParse_RealResponseBody_ReadsEveryTokenField()
    {
        var usage = TokenUsageParser.TryParse(RealResponseBody);

        usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(4574);
        usage.CachedInputTokens.ShouldBe(3840);
        usage.OutputTokens.ShouldBe(142);
        usage.ReasoningTokens.ShouldBe(0);
    }

    [Fact]
    public void CacheHitRatio_ReflectsTheCachedShareOfThePrompt()
    {
        var usage = TokenUsageParser.TryParse(RealResponseBody);

        usage.ShouldNotBeNull();
        usage.CacheHitRatio.ShouldBe(3840d / 4574d, 0.0001);
    }

    [Fact]
    public void CacheHitRatio_NoTokensBilled_IsZeroNotDivideByZero()
    {
        var usage = TokenUsageParser.TryParse("""
            {"usage": {"input_tokens": 0, "output_tokens": 0}}
            """);

        usage.ShouldNotBeNull();
        usage.CacheHitRatio.ShouldBe(0d);
    }

    /// <summary>
    ///     A cold prefix reports the details object with a zero — the exact signal that says the
    ///     cache went cold between slots, so it must survive as 0 rather than being lost.
    /// </summary>
    [Fact]
    public void TryParse_ColdCache_ReportsZeroCachedTokens()
    {
        var usage = TokenUsageParser.TryParse("""
            {"usage": {"input_tokens": 6222, "input_tokens_details": {"cached_tokens": 0},
                       "output_tokens": 142}}
            """);

        usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(6222);
        usage.CachedInputTokens.ShouldBe(0);
        usage.CacheHitRatio.ShouldBe(0d);
    }

    [Fact]
    public void TryParse_DetailsObjectsAbsent_DefaultsNestedCountsToZero()
    {
        var usage = TokenUsageParser.TryParse("""
            {"usage": {"input_tokens": 100, "output_tokens": 20}}
            """);

        usage.ShouldNotBeNull();
        usage.CachedInputTokens.ShouldBe(0);
        usage.ReasoningTokens.ShouldBe(0);
    }

    [Fact]
    public void TryParse_ReasoningModel_ReadsReasoningTokens()
    {
        var usage = TokenUsageParser.TryParse("""
            {"usage": {"input_tokens": 100, "output_tokens": 500,
                       "output_tokens_details": {"reasoning_tokens": 448}}}
            """);

        usage.ShouldNotBeNull();
        usage.ReasoningTokens.ShouldBe(448);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"error\": {\"code\": \"429\", \"message\": \"Rate limited\"}}")]
    [InlineData("{\"usage\": \"unexpectedly a string\"}")]
    public void TryParse_BodyWithoutUsableUsage_ReturnsNullWithoutThrowing(string? body)
    {
        TokenUsageParser.TryParse(body).ShouldBeNull();
    }
}
