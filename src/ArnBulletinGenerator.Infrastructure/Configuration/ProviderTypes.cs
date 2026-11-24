namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Available output providers for broadcast audio.
/// </summary>
public enum OutputProviderType
{
    Console,
    AzureBlob
}

public enum ExternalProviderMode
{
    Real,
    Fake
}
