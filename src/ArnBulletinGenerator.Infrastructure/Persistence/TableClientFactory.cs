using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Centralizes TableClient creation from <see cref="StorageOptions" />,
///     eliminating duplicate constructor logic across repositories.
/// </summary>
internal static class TableClientFactory
{
    /// <summary>
    ///     Creates a <see cref="TableClient" /> for the given table, automatically
    ///     detecting local Azurite vs. hosted Azure Table Storage.
    /// </summary>
    internal static TableClient Create(
        string tableName,
        IOptions<StorageOptions> options,
        TokenCredential credential)
    {
        var storageOptions = options.Value;
        string accountName = storageOptions.AccountName;
        bool isLocalEmulator = storageOptions.IsLocalDevelopment;

        var clientOptions = new TableClientOptions();
        clientOptions.Retry.MaxRetries = storageOptions.MaxRetries;

        return isLocalEmulator
            ? new TableClient(StorageOptions.LocalEmulatorConnectionString, tableName, clientOptions)
            : new TableServiceClient(
                    new Uri($"https://{accountName}.table.core.windows.net"),
                    credential,
                    clientOptions)
                .GetTableClient(tableName);
    }
}
