using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shouldly;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Regression test for bug-bash 2026-08-05 F1: the published JSON Schema (the embedded
///     resource served by GET /api/config/schema) rejected the platform's own export with 44
///     errors — PronunciationDictionaries.Scope, Schedules.Bookend*/minorTraffic fill source and
///     the whole Bookends section were undocumented. A full-surface bundle built from the export
///     DTOs must validate against the shipped schema with zero errors, so any future DTO/schema
///     drift fails here. The validator is a focused Draft 2020-12 subset implementing exactly the
///     keywords this schema uses (type, properties/additionalProperties, required, items, $ref,
///     enum, const, numeric/string/array bounds, pattern); format is an annotation here and is
///     deliberately not enforced (matching the evidence run's default jsonschema behaviour).
/// </summary>
public sealed class ConfigExportSchemaTests
{
    private const string ResourceName = "ArnBulletinGenerator.Functions.Resources.config-bundle.schema.json";

    [Fact]
    public void ShippedSchema_AcceptsFullSurfaceExportedBundle_WithZeroErrors()
    {
        var schema = JsonNode.Parse(LoadSchema())!;
        var bundle = JsonNode.Parse(JsonSerializer.Serialize(
            RepresentativeExportBundle(), JsonSerializationDefaults.Response))!;

        var errors = Draft202012Validator.Validate(schema, bundle);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ShippedSchema_RejectsUndocumentedProperty_ProvingTheValidatorIsNotAVacuousNoOp()
    {
        var schema = JsonNode.Parse(LoadSchema())!;
        var bundle = JsonNode.Parse(JsonSerializer.Serialize(
            RepresentativeExportBundle(), JsonSerializationDefaults.Response))!;
        bundle["VoiceProfiles"]![0]!.AsObject()["BogusField"] = "x";

        var errors = Draft202012Validator.Validate(schema, bundle);

        errors.ShouldContain(e => e.Contains("BogusField"));
    }

    [Fact]
    public void ShippedSchema_RejectsUnknownFillSource_ProvingMinorTrafficIsDocumentedByEnum()
    {
        // If the enum ever loses 'minorTraffic', a bundle using it (which the live export does)
        // must fail — the enum membership is part of the contract, not just property presence.
        var schema = JsonNode.Parse(LoadSchema())!;
        var bundle = JsonNode.Parse(JsonSerializer.Serialize(
            RepresentativeExportBundle(), JsonSerializationDefaults.Response))!;
        bundle["Schedules"]![0]!["FillOrder"]![1] = "minorTraffic"!;

        var errors = Draft202012Validator.Validate(schema, bundle);

        errors.ShouldBeEmpty();
    }

    private static string LoadSchema()
    {
        var assembly = typeof(ConfigTransferHttpTrigger).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded schema '{ResourceName}' missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    ///     One entry per top-level section and every field those sections can carry, shaped
    ///     exactly like the export builder emits (ConfigBundleDto serialized with the response
    ///     options — all properties present, nulls included). If a DTO field gains a sibling and
    ///     the schema is not updated, this bundle carries it and the validation fails.
    /// </summary>
    private static ConfigBundleDto RepresentativeExportBundle() => new(
        Schema: "https://test/api/config/schema",
        SchemaVersion: 1,
        ExportedAtUtc: "2026-08-06T00:00:00.0000000Z",
        Source: "ibp-aue-dev-fna-app01",
        VoiceProfiles:
        [
            new ConfigVoiceProfileDto(
                Id: "vp-1", Name: "Sydney Breakfast", Description: "Drive-time read",
                BaseVoiceId: "eleven_base", BaseVoiceName: "Base Voice", Model: "eleven_v3",
                Settings: new VoiceSettingsDto(1.0, 0.4, 0.75, 0.15, true),
                PaceBoostWpm: 4, ChangeNote: "import")
        ],
        DeliveryStyles:
        [
            new ConfigDeliveryStyleDto(
                Id: "ds-1", Name: "Drive-Time", Description: "Warm and calm",
                Policy: new DeliveryStylePolicyDto("Warm", ["[calm]"], 2), ChangeNote: "import")
        ],
        PronunciationDictionaries:
        [
            new ConfigPronunciationDictionaryDto(
                Id: "pd-1", Name: "Sydney Roads", Description: "Suburb respellings",
                Rules:
                [
                    new PronunciationRuleDto("alias", "Bourke", "Burk", null, false, true, false)
                ],
                ChangeNote: "import", Scope: "Station"),
            new ConfigPronunciationDictionaryDto(
                Id: "pd-global", Name: "Platform Wide", Scope: "Global")
        ],
        SafetyTerms:
        [
            new ConfigSafetyTermDto(
                Id: "st-1", Kind: "Banned", Scope: "Global", ScopeId: null,
                Term: "badword", Note: "blocked", Enabled: true)
        ],
        Guidance:
        [
            new ConfigGuidanceDto(
                Id: "g-1", Scope: "Global", ScopeId: null, Type: "Style",
                Text: "Keep it conversational", Enabled: true,
                ReviewAfterUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))
        ],
        PriorityRoads:
        [
            new PriorityRoadsDto("sydney",
                [new PriorityRoadDto("M1 Pacific Motorway", true, "high", true)])
        ],
        VerifiedPronunciations:
        [
            new ConfigVerifiedPronunciationsDto("sydney",
                [new VerifiedPronunciationDto("Keilor", "Said KEE-ler", true, "operator-1",
                    "2026-08-05T00:00:00Z")])
        ],
        OperatorAssignments:
        [
            new ConfigOperatorAssignmentDto(
                "op-1", "Operator One", ["sydney", "melbourne"],
                new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), "admin")
        ],
        VoiceCalibrations:
        [
            new ConfigVoiceCalibrationDto(
                ConfigurationKey: "voice-1|1|0|0.75|0.15|1", VoiceId: "voice-1",
                Settings: new VoiceSettingsDto(1.0, 0.0, 0.75, 0.15, true),
                Provider: "elevenlabs", SyllablesPerSecond: 4.2, SyllablesPerWord: 1.4,
                WordsPerMinute: 180, SyllablesPerMinute: 252, RunCount: 3,
                ReferenceSyllableCount: 42, ReferenceDurationSeconds: 10,
                CalibratedAt: new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                ValidForDays: 30)
        ],
        FillerStatements:
        [
            new FillerStatementsDto("sydney",
                [
                    new FillerStatementDto("f1", "Stay safe on the roads.", true),
                    new FillerStatementDto("f2", "Allow extra time at the school gates.", true,
                        new FillerAvailabilityDto(
                            StartDate: "2026-08-01", EndDate: "2026-11-30",
                            Days: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
                            StartLocal: "06:00", EndLocal: "09:00"))
                ])
        ],
        Bookends:
        [
            new ConfigBookendDto(
                Id: "bk-1", Name: "Sydney Open", Description: "Drive pair",
                IntroText: "Looking over {city} today", OutroText: "That's it for {city} traffic.",
                ChangeNote: "import")
        ],
        Schedules:
        [
            new ConfigScheduleDto(
                MarketId: "sydney", Name: "Sydney Breakfast", Station: "2GB",
                Timezone: "Australia/Sydney", CronExpression: "0 */30 6-9 * * 1-5",
                Enabled: true, TargetDurationSeconds: 30,
                NotifyBeforePlayoutMinutes: 15, EscalationBeforePlayoutMinutes: 5,
                VoiceProfileId: "vp-1", OutputPath: "sydney/traffic/",
                Coverage: new CoverageDto(-33.87, 151.21, 50, "bbox",
                    West: 150.9, South: -34.1, East: 151.5, North: -33.6),
                Content: new ContentSelectionDto(Traffic: true, Weather: true, Fuel: false,
                    WeatherRequired: true, FuelRequired: true),
                DeliveryStyleId: "ds-1", PronunciationDictionaryId: "pd-1",
                VoiceRotation: new VoiceRotationDto("timeOfDay",
                    Windows: [new VoiceWindowDto("06:00", "09:00", ["vp-1"])]),
                FillOrder: ["weather", "minorTraffic", "filler"],
                BookendId: "bk-1",
                BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bk-1"]),
                BookendTokens: new Dictionary<string, string> { ["city"] = "Sydney" },
                // A POPULATED calendar, so the schema's own calendar definition is
                // exercised rather than merely tolerated as a null. Covers a range, a
                // single date nested inside it, and a suppression.
                Calendar: new ScheduleCalendarDto(
                    [
                        new SchedulePatternDto("public-holiday", "Public holiday",
                            "0,30 7-9,15-17 * * 0,1,2,3,4,5,6"),
                        new SchedulePatternDto("reduced", "Reduced clock",
                            "0 7-9,15-17 * * 0,1,2,3,4,5,6")
                    ],
                    [
                        new ScheduleAssignmentDto("2026-12-21", "2026-12-31", "reduced",
                            Note: "Christmas to New Year"),
                        new ScheduleAssignmentDto("2026-12-25", "2026-12-25", "public-holiday",
                            Note: "Christmas Day"),
                        new ScheduleAssignmentDto("2026-12-31", "2026-12-31", null,
                            Suppress: true, Note: "New Year's Eve, off air")
                    ]))
        ]);

    /// <summary>
    ///     Minimal Draft 2020-12 validator covering exactly the keywords the shipped bundle
    ///     schema uses. Self-contained so the regression test has no external dependency; it
    ///     only needs to agree with a full validator on the failure classes we care about —
    ///     unknown/additional properties, wrong types, missing required fields, invalid enums,
    ///     and out-of-bounds values (all of which the 44-error F1 failure was).
    /// </summary>
    private sealed class Draft202012Validator(JsonNode root)
    {
        public static List<string> Validate(JsonNode schema, JsonNode instance)
        {
            var validator = new Draft202012Validator(schema);
            var errors = new List<string>();
            validator.Check(schema, instance, "$", errors);
            return errors;
        }

        private void Check(JsonNode? schema, JsonNode? instance, string path, List<string> errors)
        {
            if (schema is not JsonObject obj)
                return;

            if (obj["$ref"] is JsonValue reference)
                Check(Resolve(reference.GetValue<string>()), instance, path, errors);

            if (obj["type"] is JsonNode typeNode)
            {
                var allowed = typeNode is JsonArray array
                    ? array.Select(v => v!.GetValue<string>()).ToList()
                    : [typeNode.GetValue<string>()];
                var kind = KindOf(instance);
                var matches = allowed.Contains(kind);
                if (!matches && kind == "number")
                {
                    // Draft 2020-12: "integer" matches any JSON number with a zero fractional
                    // part (and "number" matches every JSON number), so a Number-kind value can
                    // satisfy an "integer" type when it is integral.
                    matches = allowed.Contains("integer")
                        && instance is JsonValue numberValue
                        && numberValue.GetValueKind() == JsonValueKind.Number
                        && numberValue.TryGetValue<decimal>(out var decimalValue)
                        && decimalValue == Math.Truncate(decimalValue);
                }

                if (!matches)
                    errors.Add($"{path}: expected type {string.Join("/", allowed)}, got {kind}");
            }

            if (instance is JsonObject instanceObject)
            {
                if (obj["properties"] is JsonObject properties)
                {
                    foreach (var (name, propertySchema) in properties)
                        if (instanceObject.TryGetPropertyValue(name, out var child))
                            Check(propertySchema, child, $"{path}.{name}", errors);
                }

                if (obj["additionalProperties"] is JsonValue { } additional && additional.GetValue<bool>() == false)
                {
                    var allowedNames = obj["properties"] is JsonObject propertyMap
                        ? propertyMap.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal)
                        : [];
                    foreach (var (name, _) in instanceObject)
                        if (!allowedNames.Contains(name))
                            errors.Add($"{path}: additional property '{name}' is not allowed");
                }

                if (obj["required"] is JsonArray required)
                    foreach (var nameNode in required)
                    {
                        var name = nameNode!.GetValue<string>();
                        if (!instanceObject.ContainsKey(name))
                            errors.Add($"{path}: required property '{name}' is missing");
                    }
            }

            if (instance is JsonArray instanceArray)
            {
                if (obj["items"] is JsonNode itemsSchema)
                    for (int i = 0; i < instanceArray.Count; i++)
                        Check(itemsSchema, instanceArray[i], $"{path}[{i}]", errors);

                if (obj["minItems"] is JsonValue minItems && instanceArray.Count < minItems.GetValue<int>())
                    errors.Add($"{path}: expected at least {minItems.GetValue<int>()} items, got {instanceArray.Count}");
                if (obj["maxItems"] is JsonValue maxItems && instanceArray.Count > maxItems.GetValue<int>())
                    errors.Add($"{path}: expected at most {maxItems.GetValue<int>()} items, got {instanceArray.Count}");
            }

            if (instance is JsonValue value)
            {
                if (value.GetValueKind() == JsonValueKind.Number)
                {
                    double number = value.GetValue<double>();
                    if (obj["minimum"] is JsonValue minimum && number < minimum.GetValue<double>())
                        errors.Add($"{path}: {number} is below minimum {minimum.GetValue<double>()}");
                    if (obj["maximum"] is JsonValue maximum && number > maximum.GetValue<double>())
                        errors.Add($"{path}: {number} is above maximum {maximum.GetValue<double>()}");
                    if (obj["exclusiveMinimum"] is JsonValue exclusive && number <= exclusive.GetValue<double>())
                        errors.Add($"{path}: {number} is not above exclusiveMinimum {exclusive.GetValue<double>()}");
                }
                else if (value.GetValueKind() == JsonValueKind.String)
                {
                    var text = value.GetValue<string>();
                    if (obj["minLength"] is JsonValue minLength && text.Length < minLength.GetValue<int>())
                        errors.Add($"{path}: string is shorter than minLength {minLength.GetValue<int>()}");
                    if (obj["maxLength"] is JsonValue maxLength && text.Length > maxLength.GetValue<int>())
                        errors.Add($"{path}: string is longer than maxLength {maxLength.GetValue<int>()}");
                    if (obj["pattern"] is JsonValue pattern && !Regex.IsMatch(text, pattern.GetValue<string>()))
                        errors.Add($"{path}: '{text}' does not match pattern {pattern.GetValue<string>()}");
                }
            }

            if (obj["enum"] is JsonArray enumValues && instance is not null
                && !enumValues.Any(v => JsonNode.DeepEquals(v, instance)))
            {
                errors.Add($"{path}: value {instance.ToJsonString()} is not one of {enumValues.ToJsonString()}");
            }

            if (obj["const"] is JsonNode constant && instance is not null
                && !JsonNode.DeepEquals(constant, instance))
            {
                errors.Add($"{path}: expected const {constant.ToJsonString()}, got {instance.ToJsonString()}");
            }
        }

        private JsonNode Resolve(string reference)
        {
            const string prefix = "#/$defs/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported $ref '{reference}' in schema");
            var name = reference[prefix.Length..];
            return (root["$defs"] as JsonObject)?[name]
                   ?? throw new InvalidOperationException($"Unresolvable $ref '{reference}' in schema");
        }

        private static string KindOf(JsonNode? node)
        {
            // JsonNode.Parse stores JSON null property values as C# null nodes (not JsonValue
            // instances), so a null reference must be reported as the JSON "null" kind.
            if (node is null)
                return "null";
            return node.GetValueKind() switch
            {
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Null => "null",
                _ => "unknown"
            };
        }
    }
}
