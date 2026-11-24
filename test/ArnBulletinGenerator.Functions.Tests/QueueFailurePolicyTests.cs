using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Queues;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class QueueFailurePolicyTests
{
    private const string JobId = "test-job";
    private const string Caller = "Broadcast";

    [Fact]
    public void ContentLimited_Acknowledges()
    {
        var result = Result.Failure(OperationalFailure.ContentLimited(
            "content-limited", "Content-limited: best spoken 18.2s could not fill the 30s slot"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.NotThrow(act);
    }

    [Fact]
    public void SafetyRejected_Acknowledges()
    {
        var result = Result.Failure(OperationalFailure.SafetyRejected(
            "safety-blocked", "Content safety violation: banned word found: foo"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.NotThrow(act);
    }

    [Fact]
    public void Conflict_Acknowledges()
    {
        var result = Result.Failure(OperationalFailure.Conflict(
            "terminal-state", "Job already in terminal state: Published"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.NotThrow(act);
    }

    [Fact]
    public void Permanent_Acknowledges()
    {
        var result = Result.Failure(OperationalFailure.Permanent(
            "tts-bad-request", "bad request: invalid voice"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.NotThrow(act);
    }

    [Fact]
    public void Retryable_ThrowsInvalidOperation()
    {
        var result = Result.Failure(OperationalFailure.Retryable(
            "tts-timeout", "Audio synthesis timeout"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Transient");
        ex.Message.ShouldContain(JobId);
        ex.Message.ShouldContain("Audio synthesis timeout");
    }

    [Fact]
    public void InvalidInput_ThrowsInvalidOperation()
    {
        var result = Result.Failure(OperationalFailure.InvalidInput(
            "bad-input", "Invalid broadcast request parameters"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Transient");
        ex.Message.ShouldContain(JobId);
    }

    [Fact]
    public void Cancelled_ThrowsOperationCanceled()
    {
        var result = Result.Failure(OperationalFailure.Cancelled(
            "user-cancelled", "Operation was cancelled by caller"));
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.Throw<OperationCanceledException>(act);
    }

    [Fact]
    public void UntypedLegacy_ThrowsInvalidOperation()
    {
        var result = Result.Failure("something unexpected happened");
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Transient");
        ex.Message.ShouldContain("something unexpected happened");
    }

    [Fact]
    public void UntypedLegacy_PreservesErrorString()
    {
        const string error = "unclassified orchestrator error";
        var result = Result.Failure(error);
        var ex = Should.Throw<InvalidOperationException>(
            () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller));
        ex.Message.ShouldContain(error);
    }

    [Fact]
    public void IncludesJobIdInExceptionMessage()
    {
        var result = Result.Failure(OperationalFailure.Retryable("timeout", "timeout"));
        var ex = Should.Throw<InvalidOperationException>(
            () => QueueFailurePolicy.ClassifyOrThrow(result, "j-42", "Regeneration"));
        ex.Message.ShouldContain("j-42");
        ex.Message.ShouldContain("Regeneration");
    }

    [Fact]
    public void IncludesCallerInExceptionMessage()
    {
        var result = Result.Failure(OperationalFailure.Retryable("timeout", "timeout"));
        var ex = Should.Throw<InvalidOperationException>(
            () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, "Regeneration"));
        ex.Message.ShouldContain("Regeneration");
    }

    [Fact]
    public void SuccessfulResult_ThrowsInvalidOperation()
    {
        var result = Result.Success();
        var act = () => QueueFailurePolicy.ClassifyOrThrow(result, JobId, Caller);
        Should.Throw<InvalidOperationException>(act);
    }
}
