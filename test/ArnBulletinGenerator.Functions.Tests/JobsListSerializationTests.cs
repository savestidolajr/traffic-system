using System.Text.Json;
using System.Text.Json.Nodes;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Http;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Regression test for the 2026-08-10 UI audit's highest-severity finding: every job-list
///     response silently omitted MeasuredDurationSec and DeltaSec.
///     <para>
///         <c>JobsListResponseDto.Items</c> used to declare an abstract element type
///         (<c>JobSummaryDtoBase</c>) whose derived <c>CompletedJobSummaryDto</c> carried the two
///         measurements. System.Text.Json uses the DECLARED type for collection elements — only a
///         root value is serialized by runtime type — so the derived properties were dropped from
///         the wire on every list call while job DETAIL (a root value) kept working. Live dev
///         evidence: 0 of 219 jobs carried the fields in <c>GET /api/jobs</c>, including 82
///         APPROVED, while the same job's detail returned <c>MeasuredDurationSec: 22</c>. The
///         console degraded silently (<c>api.MeasuredDurationSec ?? 0</c>), so History's Duration
///         and delta columns, both of their column sorts and the CSV export were dead.
///     </para>
///     <para>
///         The pre-existing mapping tests asserted the DTO OBJECT and so stayed green throughout.
///         These tests assert the EMITTED JSON, through the same options the HTTP layer uses.
///     </para>
/// </summary>
public sealed class JobsListSerializationTests
{
    private static readonly DateTime FixedTime = new(2026, 8, 10, 6, 54, 1, DateTimeKind.Utc);

    private static Job CompletedJob(double target = 22, double measured = 22)
    {
        var job = Job.Reconstitute(
            "027729aac185484591019faa32cc050c", "KIIS 90s", "sydney", target,
            new[] { new StatusHistoryEntry(JobStatus.Approved, FixedTime, "reviewer@arn.com.au") });
        job.MeasuredDurationSec = Option<double>.Some(measured);
        return job;
    }

    private static JsonNode SerializeList(params Job[] jobs)
    {
        var items = jobs.Select(j => j.ToSummaryDto()).ToList();
        var response = new JobsListResponseDto(items, items.Count);
        return JsonNode.Parse(JsonSerializer.Serialize(response, JsonSerializationDefaults.Response))!;
    }

    [Fact]
    public void ListResponse_CarriesMeasuredDurationAndDelta_OnTheWire()
    {
        var row = SerializeList(CompletedJob(target: 22, measured: 22))["Items"]!.AsArray()[0]!;

        // The whole point: these must be PRESENT in the JSON, not merely on the C# object.
        row["MeasuredDurationSec"].ShouldNotBeNull();
        row["DeltaSec"].ShouldNotBeNull();
        row["MeasuredDurationSec"]!.GetValue<double>().ShouldBe(22);
        row["DeltaSec"]!.GetValue<double>().ShouldBe(0);
    }

    [Fact]
    public void ListResponse_ReportsOverrunAsAPositiveDelta()
    {
        var row = SerializeList(CompletedJob(target: 22, measured: 26.5))["Items"]!.AsArray()[0]!;

        row["DeltaSec"]!.GetValue<double>().ShouldBe(4.5);
    }

    [Fact]
    public void ListResponse_EmitsNullMeasurements_ForAJobWithNoAudioYet()
    {
        var waiting = Job.Reconstitute(
            "50803125890e417b82b233f283b837e4", "7XS 92.1", "queenstown", 30,
            new[] { new StatusHistoryEntry(JobStatus.Requested, FixedTime, "system") });

        var row = SerializeList(waiting)["Items"]!.AsArray()[0]!;

        // Present-but-null, never absent: the console distinguishes "no audio yet" from
        // "audio exists and measured 0s".
        row.AsObject().ContainsKey("MeasuredDurationSec").ShouldBeTrue();
        row["MeasuredDurationSec"].ShouldBeNull();
        row["DeltaSec"].ShouldBeNull();
    }

    [Fact]
    public void ListResponse_KeepsEveryBaseFieldTheConsoleReads()
    {
        var row = SerializeList(CompletedJob())["Items"]!.AsArray()[0]!.AsObject();

        foreach (var field in new[]
                 {
                     "JobId", "CreatedAt", "Station", "Market",
                     "TargetDurationSec", "Status", "LastUpdatedAt", "Etag"
                 })
            row.ContainsKey(field).ShouldBeTrue($"list rows must still carry {field}");
    }

    // =====================================================================
    // Proof the tests above are not vacuous: the SHAPE that caused the bug,
    // reproduced locally, still loses the derived properties under the very
    // same serializer options. This is the mechanism, pinned — so nobody
    // "simplifies" the flat DTO back into a hierarchy believing it is safe.
    //
    // It also documents why the sibling DETAIL responses were never affected:
    // JobsHttpTrigger deliberately calls OkJsonAsync<object>(req, dto), and an
    // `object`-declared value IS serialized by its runtime type. The list path
    // could not use that escape hatch, because its polymorphism sat NESTED
    // inside JobsListResponseDto.Items where the root's declared type is
    // irrelevant. Hence the flat DTO.
    // =====================================================================

    private abstract record LegacySummaryBase
    {
        public string JobId { get; init; } = "";
    }

    private sealed record LegacyCompletedSummary : LegacySummaryBase
    {
        public required double MeasuredDurationSec { get; init; }
    }

    private sealed record LegacyListResponse(IReadOnlyList<LegacySummaryBase> Items);

    [Fact]
    public void NestedBaseTypedElements_DropDerivedProperties_TheDefectThisFileGuards()
    {
        var legacy = new LegacyListResponse(
            [new LegacyCompletedSummary { JobId = "job-1", MeasuredDurationSec = 22 }]);

        var row = JsonNode.Parse(
            JsonSerializer.Serialize(legacy, JsonSerializationDefaults.Response))!["Items"]!
            .AsArray()[0]!.AsObject();

        row.ContainsKey("JobId").ShouldBeTrue();
        row.ContainsKey("MeasuredDurationSec")
            .ShouldBeFalse("a base-typed collection element is serialized as the base type");
    }

    [Fact]
    public void BaseTypedRootValue_AlsoDropsDerivedProperties_SoTheEscapeHatchIsExplicitObject()
    {
        LegacySummaryBase root = new LegacyCompletedSummary { JobId = "job-1", MeasuredDurationSec = 22 };

        // Declared type = the abstract base: dropped, even at the root.
        JsonNode.Parse(JsonSerializer.Serialize(root, JsonSerializationDefaults.Response))!
            .AsObject().ContainsKey("MeasuredDurationSec").ShouldBeFalse();

        // Declared type = object: runtime type wins. This is exactly what
        // JobsHttpTrigger.GetJob relies on via OkJsonAsync<object>.
        JsonNode.Parse(JsonSerializer.Serialize<object>(root, JsonSerializationDefaults.Response))!
            .AsObject().ContainsKey("MeasuredDurationSec").ShouldBeTrue();
    }
}
