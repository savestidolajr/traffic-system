using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Functions.ConfigTransfer;

// =============================================================================
// HTTP-layer-only wrappers around the config-bundle import surface. The bundle,
// section DTOs, and import-report types live in ArnBulletinGenerator.Application
// (ConfigTransferContracts.cs) alongside ConfigImportService, which owns them.
// =============================================================================

/// <summary>202 response when the apply parked as a pending import for a second approver.</summary>
public sealed record ConfigImportAcceptedDto(
    string PendingImportId,
    ConfigImportReportDto Report);

public sealed record PendingConfigImportDto(
    string Id,
    string RequestedBy,
    string RequestedAtUtc,
    ConfigImportReportDto? Report);

public sealed record PendingConfigImportsListDto(
    IReadOnlyList<PendingConfigImportDto> Items,
    int Total);
