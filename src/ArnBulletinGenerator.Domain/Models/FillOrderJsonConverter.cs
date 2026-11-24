using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Persists a fill-source order as the enum NAMES rather than ordinals so a stored
///     <c>GeneratorInputSnapshot.FillOrder</c> stays human-auditable against the station's
///     configured order — the same readability convention the schedule's <c>FillOrderJson</c>
///     column follows. Reading is tolerant so a corrupt, hand-edited, or legacy snapshot cannot
///     throw while the corpus loader or replay harness rebuilds the context: unknown entries are
///     skipped (the rest of the order stays intact), numeric ordinals still parse, and a null or
///     missing list yields empty.
/// </summary>
public sealed class FillOrderJsonConverter : JsonConverter<IReadOnlyList<FillSource>>
{
    public override IReadOnlyList<FillSource> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected a JSON array for a fill order.");

        var parsed = new List<FillSource>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType == JsonTokenType.String
                && Enum.TryParse(reader.GetString(), ignoreCase: true, out FillSource source)
                && Enum.IsDefined(source))
            {
                parsed.Add(source);
                continue;
            }

            if (reader.TokenType == JsonTokenType.Number
                && reader.TryGetInt32(out int ordinal)
                && Enum.IsDefined(typeof(FillSource), ordinal))
            {
                parsed.Add((FillSource)ordinal);
                continue;
            }

            // Unknown entry: skip it and keep the rest of the order intact.
            reader.Skip();
        }

        return parsed;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<FillSource> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (FillSource source in value)
            writer.WriteStringValue(source.ToString());
        writer.WriteEndArray();
    }
}
