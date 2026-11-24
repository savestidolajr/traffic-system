using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class ModelResponseParserTests
{
    private static ResponseResult Deserialize(string json) =>
        ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(json))!;

    // ── IsTokenExhausted ────────────────────────────────────────────────

    [Fact]
    public void IsTokenExhausted_Completed_ReturnsFalse()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "Heavy traffic on the M4.", "annotations": []}]
              }]
            }
            """);

        ModelResponseParser.IsTokenExhausted(result).ShouldBeFalse();
    }

    [Fact]
    public void IsTokenExhausted_IncompleteMaxTokens_ReturnsTrue()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "incomplete", "model": "gpt-4o-mini",
              "incomplete_details": {"reason": "max_output_tokens"},
              "output": []
            }
            """);

        ModelResponseParser.IsTokenExhausted(result).ShouldBeTrue();
    }

    // ── ExtractRefusalText ──────────────────────────────────────────────

    [Fact]
    public void ExtractRefusalText_CompletedWithOutputText_ReturnsNull()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "Some text.", "annotations": []}]
              }]
            }
            """);

        ModelResponseParser.ExtractRefusalText(result).ShouldBeNull();
    }

    [Fact]
    public void ExtractRefusalText_RefusalContentPart_ReturnsRefusalText()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [{"type": "refusal", "refusal": "I cannot answer that request."}]
              }]
            }
            """);

        ModelResponseParser.ExtractRefusalText(result).ShouldBe("I cannot answer that request.");
    }

    // ── GetOutputText (SDK integration — preserves old ParseModelResponse text-extraction contracts) ──

    [Fact]
    public void GetOutputText_CompletedWithText_ReturnsText()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "Heavy traffic on the M4.", "annotations": []}]
              }]
            }
            """);

        result.GetOutputText().ShouldBe("Heavy traffic on the M4.");
    }

    [Fact]
    public void GetOutputText_CompletedWithOutputArray_JoinsContentText()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [
                  {"type": "output_text", "text": "First line.", "annotations": []},
                  {"type": "output_text", "text": "Second line.", "annotations": []}
                ]
              }]
            }
            """);

        result.GetOutputText().ShouldContain("First line.");
        result.GetOutputText().ShouldContain("Second line.");
    }

    // ── BuildResponseDiagnostic ─────────────────────────────────────────

    [Fact]
    public void BuildResponseDiagnostic_CompletedOnly_ReturnsJustStatus()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "completed", "model": "gpt-4o-mini",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "Hello", "annotations": []}]
              }]
            }
            """);

        ModelResponseParser.BuildResponseDiagnostic(result).ShouldBe("status=Completed");
    }

    [Fact]
    public void BuildResponseDiagnostic_WithIncomplete_IncludesReason()
    {
        var result = Deserialize("""
            {
              "id": "resp_1", "object": "response", "created_at": 1700000000,
              "status": "incomplete", "model": "gpt-4o-mini",
              "incomplete_details": {"reason": "max_output_tokens"},
              "output": []
            }
            """);

        string diagnostic = ModelResponseParser.BuildResponseDiagnostic(result);
        diagnostic.ShouldContain("status=Incomplete");
        diagnostic.ShouldContain("reason=max_output_tokens");
    }

    [Fact]
    public void BuildResponseDiagnostic_WithRefusal_IncludesTruncatedRefusal()
    {
        string longRefusal = new string('x', 200);
        var result = Deserialize(JsonSerializer.Serialize(new
        {
            id = "resp_1",
            @object = "response",
            created_at = 1700000000,
            status = "completed",
            model = "gpt-4o-mini",
            output = new[]
            {
                new
                {
                    type = "message",
                    id = "msg_1",
                    status = "completed",
                    role = "assistant",
                    content = new[] { new { type = "refusal", refusal = longRefusal } }
                }
            }
        }));

        string diagnostic = ModelResponseParser.BuildResponseDiagnostic(result);
        diagnostic.ShouldContain("status=Completed");
        diagnostic.ShouldContain("refusal=");
        diagnostic.Length.ShouldBeLessThan(300);
    }

    // ── Truncate ────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_ShortText_ReturnsAsIs()
    {
        ModelResponseParser.Truncate("Short text", 100).ShouldBe("Short text");
    }

    [Fact]
    public void Truncate_LongText_TruncatesWithEllipsis()
    {
        string text = new string('a', 50);
        string result = ModelResponseParser.Truncate(text, 10);
        result.ShouldBe("aaaaaaaaaa...");
        result.Length.ShouldBe(13);
    }

    [Fact]
    public void Truncate_Null_ReturnsEmpty()
    {
        ModelResponseParser.Truncate(null!, 10).ShouldBeEmpty();
    }

    [Fact]
    public void Truncate_ReplacesNewlinesWithSpaces()
    {
        string result = ModelResponseParser.Truncate("line1\nline2\r\nline3", 50);
        result.ShouldNotContain("\n");
        result.ShouldContain("line1 line2 line3");
    }
}
