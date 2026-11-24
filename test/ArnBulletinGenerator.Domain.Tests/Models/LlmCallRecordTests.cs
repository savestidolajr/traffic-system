using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public sealed class LlmCallRecordTests
{
    [Fact]
    public void Error_can_capture_failure_detail_and_defaults_to_null()
    {
        // Two writeFlow calls returned after 60,002ms and 60,007ms with an empty response body and
        // zero tokens; without Error, that total upstream failure was indistinguishable from a
        // successful empty reply.
        var failed = new LlmCallRecord(
            JobId: "job-1",
            Stage: "WriteFlow",
            Attempt: 1,
            Model: "gpt-5.6-luna",
            SystemInstructions: "system",
            RenderedPrompt: "user",
            RequestBody: "{}",
            ResponseBody: "",
            ElapsedMs: 60002,
            TimestampUtc: DateTimeOffset.UtcNow,
            Error: "HTTP 500: no healthy upstream");

        // Prevents failed captures from losing the upstream error that explains an empty response.
        failed.Error.ShouldBe("HTTP 500: no healthy upstream");

        var existingShape = new LlmCallRecord(
            "job-2",
            "WriteFlow",
            1,
            "gpt-5.6-luna",
            "system",
            "user",
            "{}",
            "{}",
            120,
            DateTimeOffset.UtcNow);

        // Prevents adding Error from breaking existing positional captures by changing their default.
        existingShape.Error.ShouldBeNull();
    }
}
