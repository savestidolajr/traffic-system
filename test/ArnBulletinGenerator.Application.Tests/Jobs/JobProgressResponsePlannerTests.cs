using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Jobs;

public sealed class JobProgressResponsePlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("\"v99\"")]
    public void Plan_WhenIfNoneMatchIsAbsentOrDifferent_ReturnsOkWithVersionEtag(string? ifNoneMatch)
    {
        var document = DocumentAtVersion(2);

        var plan = JobProgressResponsePlanner.Plan(document, ifNoneMatch);

        plan.Kind.ShouldBe(JobProgressResponseKind.Ok);
        plan.ETag.ShouldBe("\"v2\"");
    }

    [Fact]
    public void Plan_WhenIfNoneMatchEqualsVersionEtag_ReturnsNotModified()
    {
        var plan = JobProgressResponsePlanner.Plan(DocumentAtVersion(2), "\"v2\"");

        plan.Kind.ShouldBe(JobProgressResponseKind.NotModified);
        plan.ETag.ShouldBe("\"v2\"");
    }

    [Fact]
    public void Plan_WhenIfNoneMatchUsesWeakPrefix_ReturnsNotModified()
    {
        var plan = JobProgressResponsePlanner.Plan(DocumentAtVersion(2), "W/\"v2\"");

        plan.Kind.ShouldBe(JobProgressResponseKind.NotModified);
        plan.ETag.ShouldBe("\"v2\"");
    }

    [Fact]
    public void Plan_WhenDocumentIsNull_ReturnsNotFoundWithoutAnEtag()
    {
        var plan = JobProgressResponsePlanner.Plan(null, null);

        plan.Kind.ShouldBe(JobProgressResponseKind.NotFound);
        plan.ETag.ShouldBeNull();
    }

    private static JobProgressDocument DocumentAtVersion(int version)
    {
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, Start);
        for (int i = 0; i < version; i++)
            document.Append(Start.AddSeconds(i), 1, ProgressStage.Evidence, ProgressEventKind.Note, "Progress note.");
        return document;
    }
}
