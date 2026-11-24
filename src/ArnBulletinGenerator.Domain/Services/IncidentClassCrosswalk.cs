using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
/// Canonical incident classes used by the multi-source traffic pipeline. The crosswalk is deliberately
/// source-scoped: the same authority word is not assumed to have the same meaning elsewhere, and the
/// 27,801-record corpus showed that a small vocabulary still grows as new incident types go live.
/// </summary>
public enum IncidentClass
{
    Crash,
    Breakdown,
    Flooding,
    Roadworks,
    Closure,
    Hazard,
    Congestion,
    SpecialEvent,
    Ferry,
    Fire
}

/// <summary>
/// Maps observed authority category and subcategory values to a canonical <see cref="IncidentClass" />.
/// Unknown values return <see cref="Option{T}.None" /> so coverage reporting can preserve the exact
/// source value and add it deliberately later; no substring or default classification is attempted.
/// </summary>
public static class IncidentClassCrosswalk
{
    private sealed record SourceVocabulary(
        IReadOnlyDictionary<string, IncidentClass> Categories,
        IReadOnlyDictionary<string, IncidentClass> SubCategories,
        IReadOnlySet<string> VehicleDetails,
        IReadOnlySet<string> NoneValues);

    private static readonly IReadOnlyDictionary<string, SourceVocabulary> Sources =
        new Dictionary<string, SourceVocabulary>(StringComparer.OrdinalIgnoreCase)
        {
            ["here"] = Create(
                ("construction", IncidentClass.Roadworks),
                ("roadClosure", IncidentClass.Closure),
                ("disabledVehicle", IncidentClass.Breakdown),
                ("congestion", IncidentClass.Congestion),
                ("roadHazard", IncidentClass.Hazard),
                ("accident", IncidentClass.Crash),
                ("other", IncidentClass.Hazard),
                ("plannedEvent", IncidentClass.SpecialEvent)),

            ["livetraffic-nsw"] = Create(
                categories:
                [
                    ("ADVERSE WEATHER", IncidentClass.Hazard),
                    ("CHANGED TRAFFIC CONDITIONS", IncidentClass.Congestion),
                    ("HAZARD", IncidentClass.Hazard),
                    ("CRASH", IncidentClass.Crash),
                    ("EMERGENCY ROADWORK", IncidentClass.Roadworks),
                    ("BREAKDOWN", IncidentClass.Breakdown),
                    ("SCHEDULED ROADWORK", IncidentClass.Roadworks),
                    ("FERRY OUT OF SERVICE", IncidentClass.Ferry),
                    // Harvested from the live unknown-vocabulary worklist (dev, 2026-08-19) after a
                    // Sydney fetch surfaced it, and independently present once in the first corpus
                    // snapshot. Signals not cycling normally are a driver hazard, which is how NSW
                    // itself groups comparable faults. This entry is the harvest loop closing: the
                    // value now classifies deterministically and no longer needs the fallback path.
                    ("TRAFFIC LIGHTS FLASHING YELLOW", IncidentClass.Hazard)
                ],
                subCategories:
                [
                    ("Water over road", IncidentClass.Flooding),
                    ("Road damage", IncidentClass.Hazard),
                    ("Planned Police operation", IncidentClass.SpecialEvent),
                    ("Maintenance", IncidentClass.Roadworks),
                    ("Resurfacing", IncidentClass.Roadworks),
                    ("Bridge work", IncidentClass.Roadworks),
                    ("Upgrade", IncidentClass.Roadworks),
                    ("Intersection upgrade", IncidentClass.Roadworks),
                    ("Utilities", IncidentClass.Roadworks),
                    ("Slope stabilisation", IncidentClass.Roadworks),
                    ("Line marking", IncidentClass.Roadworks),
                    ("Power pole/line repairs", IncidentClass.Roadworks),
                    ("Surveying", IncidentClass.Roadworks),
                    ("Road/lane widening", IncidentClass.Roadworks),
                    ("Routine maintenance", IncidentClass.Roadworks),
                    ("Warringah Freeway Upgrade", IncidentClass.Roadworks),
                    ("Signage", IncidentClass.Roadworks)
                ],
                vehicleDetails:
                ["Oversize vehicle", "Semi-trailer", "2 cars", "Car", "2 trucks", "Van"]),

            ["qldtraffic"] = Create(
                categories:
                [
                    ("Roadworks", IncidentClass.Roadworks),
                    ("Hazard", IncidentClass.Hazard),
                    ("Flooding", IncidentClass.Flooding),
                    ("Special event", IncidentClass.SpecialEvent),
                    ("Crash", IncidentClass.Crash),
                    ("Congestion", IncidentClass.Congestion)
                ],
                subCategories:
                [
                    ("Planned roadworks", IncidentClass.Roadworks),
                    ("Adverse driving conditions", IncidentClass.Hazard),
                    ("Road damage", IncidentClass.Hazard),
                    ("Bridge or culvert damaged", IncidentClass.Hazard),
                    ("Long-term flooding", IncidentClass.Flooding),
                    ("Stationary vehicle", IncidentClass.Breakdown),
                    ("Multi-vehicle", IncidentClass.Crash),
                    ("Single vehicle", IncidentClass.Crash),
                    ("Recurring", IncidentClass.Congestion),
                    ("Flash flooding", IncidentClass.Flooding),
                    ("Fire", IncidentClass.Fire),
                    ("Poor visibility", IncidentClass.Hazard)
                ],
                noneValues: ["N/A"]),

            ["mainroads-wa"] = Create(
                categories:
                [
                    ("Break Down / Tow Away", IncidentClass.Breakdown),
                    ("Vehicle Fire", IncidentClass.Fire),
                    ("Pothole / Road Surface Damage", IncidentClass.Hazard),
                    ("Road Crash", IncidentClass.Crash),
                    // These two values appeared in the second snapshot. They remain exact entries,
                    // rather than substring guesses from the incident's prose/location.
                    ("Vehicle In Emergency Bay", IncidentClass.Breakdown),
                    ("Emergency Stopping Bay (SF)", IncidentClass.Hazard)
                ],
                subCategories:
                [
                    ("Maintenance", IncidentClass.Roadworks),
                    ("Upgrades", IncidentClass.Roadworks),
                    ("Utility works", IncidentClass.Roadworks),
                    ("Resurfacing", IncidentClass.Roadworks),
                    ("PTA Works", IncidentClass.Roadworks),
                    // WA publishes these as TrafficCondition values. They describe lane state,
                    // but are still observed incident evidence when no separate class is present;
                    // retain them as hazards rather than silently dropping the source signal.
                    ("Left Emergency Lane Blocked", IncidentClass.Hazard),
                    ("All Lanes Open", IncidentClass.Hazard),
                    ("Lane Closures Unknown", IncidentClass.Hazard)
                ]),

            ["trafficsa"] = Create(
                categories:
                [
                    ("INCIDENT", IncidentClass.Hazard),
                    ("COLLISION", IncidentClass.Crash),
                    ("FLOODING", IncidentClass.Flooding),
                    ("EMERGENCY WATERWORKS", IncidentClass.Roadworks)
                ],
                subCategories:
                [
                    ("Hazard", IncidentClass.Hazard),
                    ("Urgent Dept. For Infrastructure & Transport Works", IncidentClass.Roadworks),
                    ("Collision", IncidentClass.Crash),
                    ("Flooding Water Over Road", IncidentClass.Flooding),
                    ("Ferry Closed", IncidentClass.Ferry),
                    ("Debris On Road", IncidentClass.Hazard),
                    ("Emergency Waterworks", IncidentClass.Roadworks),
                    ("Urgent Waterworks", IncidentClass.Roadworks),
                    ("Breakdown", IncidentClass.Breakdown)
                ])
        };


    /// <summary>
    /// Classifies one authority category/subcategory pair. An explicit Qld <c>N/A</c> value is
    /// intentionally unclassifiable even when a companion field happens to be populated.
    /// </summary>
    public static Option<IncidentClass> Classify(string sourceId, string? category, string? subCategory)
    {
        if (!Sources.TryGetValue(sourceId ?? string.Empty, out var source))
            return Option<IncidentClass>.None;

        string? categoryValue = Normalise(category);
        string? subCategoryValue = Normalise(subCategory);

        if (categoryValue is null && subCategoryValue is null)
            return Option<IncidentClass>.None;

        if ((categoryValue is not null && source.NoneValues.Contains(categoryValue))
            || (subCategoryValue is not null && source.NoneValues.Contains(subCategoryValue)))
            return Option<IncidentClass>.None;

        // A category is authoritative when recognised. This resolves observed vehicle-detail
        // subcategories (Car, Semi-trailer, etc.) to the parent Crash/Breakdown class; vehicle
        // type remains a separate signal and is deliberately not a class in this table.
        if (categoryValue is not null && source.Categories.TryGetValue(categoryValue, out var categoryClass))
            return Option<IncidentClass>.Some(categoryClass);

        if (subCategoryValue is not null && source.SubCategories.TryGetValue(subCategoryValue, out var subCategoryClass))
            return Option<IncidentClass>.Some(subCategoryClass);

        // Vehicle details have no standalone class. If the parent category was absent or unknown,
        // fail closed rather than inventing Crash or Breakdown.
        return Option<IncidentClass>.None;
    }

    /// <summary>
    /// Returns every observed vocabulary value encoded for <paramref name="sourceId" />, including
    /// explicit no-class sentinels such as Qld <c>N/A</c>. The returned set is a defensive read-only
    /// view and is empty for an unregistered source.
    /// </summary>
    public static IReadOnlySet<string> KnownVocabulary(string sourceId)
    {
        if (!Sources.TryGetValue(sourceId ?? string.Empty, out var source))
            return EmptyVocabulary;

        return source.Categories.Keys
            .Concat(source.SubCategories.Keys)
            .Concat(source.VehicleDetails)
            .Concat(source.NoneValues)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly IReadOnlySet<string> EmptyVocabulary =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static SourceVocabulary Create(
        params (string Value, IncidentClass Class)[] categories) =>
        Create(categories, [], []);

    private static SourceVocabulary Create(
        (string Value, IncidentClass Class)[]? categories = null,
        (string Value, IncidentClass Class)[]? subCategories = null,
        string[]? vehicleDetails = null,
        string[]? noneValues = null) =>
        new(
            ToMap(categories),
            ToMap(subCategories),
            (vehicleDetails ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
            (noneValues ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, IncidentClass> ToMap(
        (string Value, IncidentClass Class)[]? entries) =>
        (entries ?? [])
            .ToDictionary(x => x.Value, x => x.Class, StringComparer.OrdinalIgnoreCase);

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
