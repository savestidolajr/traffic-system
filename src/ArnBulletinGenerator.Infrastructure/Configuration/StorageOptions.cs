using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Azure Storage connection settings (account, container, retry policy) shared by every blob
///     and table repository, with an Azurite local-development detector.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public const string LocalEmulatorAccount = "devstoreaccount1";
    public const string LocalEmulatorConnectionString = "UseDevelopmentStorage=true";

    /// <summary>
    ///     Indicates local development (Azurite) based on the configured account name or
    ///     the Azure SDK's development-storage connection-string sentinel.
    /// </summary>
    public bool IsLocalDevelopment
    {
        get
        {
            string accountName = AccountName?.Trim() ?? string.Empty;
            return accountName.Length == 0
                   || string.Equals(accountName, LocalEmulatorAccount, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(accountName, LocalEmulatorConnectionString, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Required] public string AccountName { get; set; } = string.Empty;

    [Required] public string ContainerName { get; set; } = "broadcasts";

    /// <summary>
    ///     Max SDK retry attempts for table/blob transport failures. Default 3 matches the Azure SDK
    ///     default (4 total tries). Set to 0 in tests/eval so calls against an unreachable account fail
    ///     fast instead of stalling on the retry backoff.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    ///     Whether repositories should provision a missing hosted table on first use. Local
    ///     development (Azurite) always provisions regardless of this value.
    /// </summary>
    public bool RuntimeEnsureEnabled { get; set; } = true;
}
