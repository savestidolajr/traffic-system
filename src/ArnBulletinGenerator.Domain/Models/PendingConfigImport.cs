namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A configuration import awaiting a second approver. <see cref="ReportJson" /> is the
///     serialized dry-run report shown to the approver; <see cref="BundleJson" /> is the exact
///     bundle to apply on approval (re-validated at approval time — the environment may have
///     drifted since the request). Requester identity is captured so approval by the same
///     principal can be refused.
/// </summary>
public sealed record PendingConfigImport(
    string Id,
    string RequestedBy,
    string RequestedByObjectId,
    DateTime RequestedAtUtc,
    string ReportJson,
    string BundleJson);
