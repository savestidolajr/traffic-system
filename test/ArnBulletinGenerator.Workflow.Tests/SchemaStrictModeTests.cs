using System.Text.Json;
using ArnBulletinGenerator.Workflow.Prompts;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     Every response schema must satisfy Azure OpenAI structured-output STRICT mode.
///     <para>
///         Strict mode requires that, at every object level, <c>required</c> lists every key in
///         <c>properties</c> and <c>additionalProperties</c> is false. A schema that breaks either
///         rule is rejected by the service at call time with an HTTP 400, which surfaces as a stage
///         failure minutes into a live run rather than as a build error.
///     </para>
///     <para>
///         Measured 2026-08-26: adding a <c>members</c> array to the plan schema omitted
///         <c>requiredFacts</c> and <c>optionalFacts</c> from the member object's <c>required</c>
///         list. The API rejected every planning call. Nothing offline caught it, so it was found by
///         spending a live run. This test is that missing gate, and it needs no network.
///     </para>
/// </summary>
public sealed class SchemaStrictModeTests
{
    [Theory]
    [InlineData("plan")]
    [InlineData("compose")]
    [InlineData("ground")]
    [InlineData("writeIncidents")]
    [InlineData("writeFlow")]
    [InlineData("edit")]
    public void Schema_satisfies_strict_mode_at_every_object_level(string role)
    {
        using var doc = JsonDocument.Parse(PromptLibrary.Schema(role));
        var violations = new List<string>();
        Walk(doc.RootElement, $"{role}:root", violations);
        violations.ShouldBeEmpty(
            $"{role}.json breaks Azure strict mode and the service will reject it at call time:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("compose")]
    [InlineData("ground")]
    [InlineData("writeIncidents")]
    [InlineData("writeFlow")]
    [InlineData("edit")]
    public void Schema_is_parseable_json(string role)
    {
        Should.NotThrow(() => JsonDocument.Parse(PromptLibrary.Schema(role)));
    }

    private static void Walk(JsonElement node, string path, List<string> violations)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            var keys = properties.EnumerateObject().Select(p => p.Name).ToList();

            var required = node.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
                ? req.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [];

            foreach (string missing in keys.Where(k => !required.Contains(k)))
                violations.Add($"{path}: property '{missing}' is not listed in 'required'");

            bool additionalPropertiesClosed =
                node.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False;
            if (!additionalPropertiesClosed)
                violations.Add($"{path}: 'additionalProperties' must be present and false");

            foreach (var property in properties.EnumerateObject())
                Walk(property.Value, $"{path}.{property.Name}", violations);
        }

        if (node.TryGetProperty("items", out var items))
            Walk(items, $"{path}[]", violations);
    }
}
