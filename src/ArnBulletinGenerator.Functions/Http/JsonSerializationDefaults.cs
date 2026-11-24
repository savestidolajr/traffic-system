using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     Default JSON serialization options for HTTP requests and responses.
/// </summary>
public static class JsonSerializationDefaults
{
    /// <summary>
    ///     Options for serializing responses: include all properties.
    /// </summary>
    public static readonly JsonSerializerOptions Response = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null
    };

    /// <summary>
    ///     Options for deserializing requests: case-insensitive property names. Enums accept
    ///     their string names only — <c>allowIntegerValues: false</c> rejects raw integers so an
    ///     out-of-range value (e.g. <c>"Scope": 99</c>) fails parsing instead of being persisted
    ///     as an undefined enum member.
    /// </summary>
    public static readonly JsonSerializerOptions Request = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };
}
