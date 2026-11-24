using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Azure appends a <c>RequestId:</c>/<c>Time:</c> diagnostic block to every storage error, and
///     the repositories surface <c>ex.Message</c> straight into an API response. On dev (2026-07-28)
///     an over-ceiling write returned that block verbatim to the caller. Strip the diagnostics, but
///     keep the human-readable first line — the HTTP layer still matches "not found" and "conflict"
///     in the message text, so replacing it wholesale would turn 404s and 409s into 500s.
/// </summary>
public class StorageMessageSanitizationTests
{
    [Fact]
    public void RequestIdAndTimeBlock_IsStripped()
    {
        const string raw =
            "The property value exceeds the maximum allowed size (64KB). If the property value is a "
            + "string, it is UTF-16 encoded and the maximum number of characters should be 32K or less.\n"
            + "RequestId:4203a582-e002-000c-4197-1e289d000000\n"
            + "Time:2026-07-28T13:45:00.1234567Z";

        string clean = TableRepositoryBase.SanitizeStorageMessage(raw);

        clean.ShouldNotContain("RequestId");
        clean.ShouldNotContain("Time:");
        clean.ShouldStartWith("The property value exceeds the maximum allowed size");
    }

    [Fact]
    public void NotFoundWording_SurvivesSoThe404MappingStillWorks()
    {
        const string raw = "The specified resource was not found.\nRequestId:abc\nTime:2026-07-28T00:00:00Z";

        string clean = TableRepositoryBase.SanitizeStorageMessage(raw);

        clean.ShouldContain("not found");
        clean.ShouldNotContain("RequestId");
    }

    [Fact]
    public void ConflictWording_SurvivesSoThe409MappingStillWorks()
    {
        const string raw = "The update condition specified in the request was not satisfied. Conflict.\n"
                           + "RequestId:def\nTime:2026-07-28T00:00:00Z";

        string clean = TableRepositoryBase.SanitizeStorageMessage(raw);

        clean.ShouldContain("Conflict");
        clean.ShouldNotContain("RequestId");
    }

    [Fact]
    public void MessageWithoutDiagnostics_IsUnchanged()
    {
        const string raw = "Something went wrong.";

        TableRepositoryBase.SanitizeStorageMessage(raw).ShouldBe(raw);
    }
}
