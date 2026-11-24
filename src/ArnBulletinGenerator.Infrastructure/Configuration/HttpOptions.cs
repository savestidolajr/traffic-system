namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     HTTP transport behaviour switches.
/// </summary>
public sealed class HttpOptions
{
    public const string SectionName = "Http";

    /// <summary>
    ///     Enables compression for JSON responses when the client advertises gzip or Brotli.
    ///     Disabled by default to preserve the existing response bytes and transport behaviour.
    /// </summary>
    public bool ResponseCompressionEnabled { get; set; } = false;
}
