using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>One planned entity change: the report row plus the deferred apply action.</summary>
public sealed record PlannedConfigItem(
    ConfigImportItemDto Report,
    Func<CancellationToken, Task<Result>>? Apply);
