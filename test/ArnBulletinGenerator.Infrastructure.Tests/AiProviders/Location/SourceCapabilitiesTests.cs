using System.Text.Json;

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public sealed class SourceCapabilitiesTests
{
    [Fact]
    public void RegisteredFeeds_DeclareValidCanonicalCapabilities()
    {
        var options = Options.Create(new OfficialFeedsOptions());
        IOfficialTrafficFeed[] feeds =
        [
            new LiveTrafficNswFeed(new HttpClient(), options, NullLogger<LiveTrafficNswFeed>.Instance),
            new QldTrafficFeed(new HttpClient(), options, NullLogger<QldTrafficFeed>.Instance),
            new VicTrafficFeed(new HttpClient(), options, NullLogger<VicTrafficFeed>.Instance),
            new MainRoadsWaFeed(new HttpClient(), options, NullLogger<MainRoadsWaFeed>.Instance),
            new TrafficSaFeed(new HttpClient(), options, NullLogger<TrafficSaFeed>.Instance),
            new TasAlertFeed(new HttpClient(), options, NullLogger<TasAlertFeed>.Instance)
        ];

        foreach (var feed in feeds)
        {
            feed.SourceId.ShouldNotBeNullOrWhiteSpace();
            feed.Capabilities.SourceId.ShouldBe(feed.SourceId);
            feed.Capabilities.SchemaVersion.ShouldNotBeNullOrWhiteSpace();
            feed.Capabilities.StructuredFields
                .Intersect(feed.Capabilities.ProseFields, StringComparer.Ordinal)
                .ShouldBeEmpty();
            feed.Capabilities.StructuredFields
                .Concat(feed.Capabilities.ProseFields)
                .ShouldAllBe(field => ParsedField.All.Contains(field, StringComparer.Ordinal));
        }

        // These feeds are configured but cannot currently contribute canonical fields. Keep the
        // breakage explicit in the schema marker instead of silently advertising aspirational data.
        feeds.Single(feed => feed.SourceId == "victraffic").Capabilities
            .StructuredFields.ShouldBeEmpty();
        feeds.Single(feed => feed.SourceId == "victraffic").Capabilities
            .ProseFields.ShouldBeEmpty();
        feeds.Single(feed => feed.SourceId == "victraffic").Capabilities.SchemaVersion
            .ShouldContain("unprovisioned");
        feeds.Single(feed => feed.SourceId == "tasalert").Capabilities
            .StructuredFields.ShouldBeEmpty();
        feeds.Single(feed => feed.SourceId == "tasalert").Capabilities
            .ProseFields.ShouldBeEmpty();
        feeds.Single(feed => feed.SourceId == "trafficsa").Capabilities
            .StructuredFields.ShouldContain(ParsedField.LanesClosed);
    }

    [Fact]
    public void PositiveIntProperty_TreatsNonPositiveTrafficSentinelsAsAbsent()
    {
        foreach (var json in new[] { """{"value":0}""", """{"value":-1}""", """{"value":""}""" })
        {
            using var document = JsonDocument.Parse(json);
            OfficialFeedGeoJson.PositiveIntProperty(document.RootElement, "value").ShouldBeNull();
        }

        using var positive = JsonDocument.Parse("""{"value":2}""");
        OfficialFeedGeoJson.PositiveIntProperty(positive.RootElement, "value").ShouldBe(2);
    }

    [Fact]
    public void FromOrigins_FoldsStructuredProseAndAbsentCountsToRecordCount()
    {
        var capabilities = new SourceCapabilities(
            "fixture",
            "v1",
            new HashSet<string>(StringComparer.Ordinal) { ParsedField.RoadName },
            new HashSet<string>(StringComparer.Ordinal) { ParsedField.Direction });
        IReadOnlyList<IReadOnlyDictionary<string, FieldOrigin>> origins =
        [
            new Dictionary<string, FieldOrigin>
            {
                [ParsedField.RoadName] = FieldOrigin.Structured,
                [ParsedField.Direction] = FieldOrigin.Prose
            },
            new Dictionary<string, FieldOrigin>
            {
                [ParsedField.RoadName] = FieldOrigin.Prose
            },
            new Dictionary<string, FieldOrigin>()
        ];

        var report = SourceParseReport.FromOrigins(capabilities, origins);

        report.Records.ShouldBe(3);
        report.Fields.Count.ShouldBe(2);
        report.Fields.Single(field => field.Field == ParsedField.RoadName)
            .ShouldBe(new FieldCoverage(ParsedField.RoadName, 1, 1, 1));
        report.Fields.Single(field => field.Field == ParsedField.Direction)
            .ShouldBe(new FieldCoverage(ParsedField.Direction, 0, 1, 2));
        report.Fields.ShouldAllBe(field => field.Structured + field.Prose + field.Absent == report.Records);
    }
}
