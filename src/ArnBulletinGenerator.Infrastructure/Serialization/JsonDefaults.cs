using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArnBulletinGenerator.Infrastructure.Serialization;

/// <summary>
///     Shared <see cref="JsonSerializerOptions" /> instances to avoid redundant allocations.
/// </summary>
internal static class JsonDefaults
{
    /// <summary>
    ///     Case-insensitive deserialization with <see cref="OptionJsonConverterFactory" /> for Table Storage entities.
    /// </summary>
    public static readonly JsonSerializerOptions Storage = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new OptionJsonConverterFactory() }
    };

    /// <summary>
    ///     Minimal case-insensitive options for queue message serialization.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new OptionJsonConverterFactory() }
    };

    /// <summary>
    ///     Relaxed encoding for notification payloads containing emoji and special characters
    ///     (e.g. Webhook → Teams).
    /// </summary>
    public static readonly JsonSerializerOptions Notification = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new OptionJsonConverterFactory() }
    };
}
