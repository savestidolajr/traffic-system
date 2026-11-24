namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Provider configuration for output services.
///     Controls whether broadcast audio is written to Azure Blob Storage or the local console.
/// </summary>
public sealed class ProviderOptions
{
    public const string SectionName = "Providers";

    public OutputProviderType OutputProvider { get; init; } = OutputProviderType.Console;

    public ExternalProviderMode ExternalProviderMode { get; init; } = ExternalProviderMode.Real;

    public bool IsAzureBlobOutput => OutputProvider == OutputProviderType.AzureBlob;

    public bool UseFakeExternalProviders => ExternalProviderMode == ExternalProviderMode.Fake;
}
