using ArnBulletinGenerator.Functions.Triggers.Http;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Guards the health probe's credential rule. On 2026-08-05 ElevenLabs began refusing the
///     platform's legacy 64-character API key at ~22:20Z; every bulletin fell back or failed for
///     hours while <c>/api/health</c> reported <c>elevenLabs=True</c> with detail "HTTP 401",
///     because the probe sent no credential and accepted any response as proof of life. These tests
///     pin the distinction that outage turned on: an UNAUTHENTICATED probe measures reachability, an
///     AUTHENTICATED one measures usability, and a refused credential is never healthy.
/// </summary>
public class HealthHttpTriggerTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(400)] // ElevenLabs' response to a malformed/legacy key
    public void EvaluateProbe_CredentialSentAndRejected_IsUnhealthy_EvenWhenAnyResponseIsAccepted(
        int statusCode)
    {
        var (ok, detail) = HealthHttpTrigger.EvaluateProbe(
            statusCode, acceptAnyHttpResponse: true, credentialSent: true);

        ok.ShouldBeFalse();
        detail.ShouldContain("credential REJECTED");
        detail.ShouldContain(statusCode.ToString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(400)]
    public void EvaluateProbe_NoCredentialSent_AuthStatusStillProvesReachability(int statusCode)
    {
        // The HERE probe deliberately sends no key: it only asks "is the host reachable", so a 401
        // is an acceptable answer. Tightening this would produce false alarms, not detection.
        var (ok, detail) = HealthHttpTrigger.EvaluateProbe(
            statusCode, acceptAnyHttpResponse: true, credentialSent: false);

        ok.ShouldBeTrue();
        detail.ShouldBe($"HTTP {statusCode}");
    }

    [Fact]
    public void EvaluateProbe_CredentialSentAndAccepted_IsHealthy()
    {
        var (ok, detail) = HealthHttpTrigger.EvaluateProbe(
            200, acceptAnyHttpResponse: true, credentialSent: true);

        ok.ShouldBeTrue();
        detail.ShouldBe("HTTP 200");
        detail.ShouldNotContain("REJECTED");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void EvaluateProbe_ServerErrorWithCredential_IsNotReportedAsACredentialProblem(
        int statusCode)
    {
        // A provider outage must not be misattributed to the key, or an operator rotates a
        // perfectly good secret chasing someone else's 500.
        var (ok, detail) = HealthHttpTrigger.EvaluateProbe(
            statusCode, acceptAnyHttpResponse: true, credentialSent: true);

        ok.ShouldBeTrue(); // acceptAnyHttpResponse still tolerates a transient upstream fault
        detail.ShouldNotContain("credential REJECTED");
    }

    [Fact]
    public void EvaluateProbe_StrictProbe_RequiresSuccessStatus()
    {
        var (ok, _) = HealthHttpTrigger.EvaluateProbe(
            404, acceptAnyHttpResponse: false, credentialSent: false);

        ok.ShouldBeFalse();
    }
}
