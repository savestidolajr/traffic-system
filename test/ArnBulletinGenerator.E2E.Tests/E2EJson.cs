using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.E2E.Tests;

internal static class E2EJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
