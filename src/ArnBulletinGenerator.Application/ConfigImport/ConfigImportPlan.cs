using ArnBulletinGenerator.Application.ConfigTransfer;
namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>
///     The validated import plan. All-or-nothing gate: any <see cref="ConfigImportActions.Error" />
///     item blocks the whole apply — a partially-valid AI-edited bundle never half-lands.
/// </summary>
public sealed class ConfigImportPlan(IReadOnlyList<PlannedConfigItem> items)
{
    public IReadOnlyList<PlannedConfigItem> Items { get; } = items;

    public bool HasErrors => Items.Any(i => i.Report.Action == ConfigImportActions.Error);

    /// <summary>
    ///     Builds the operator-facing report. Counters follow the OUTCOME once applied, not the
    ///     planned action: an item planned as a create that failed to write is an error, not a
    ///     create. Counting the plan alone made a failed apply read as
    ///     <c>Creates=1, Errors=0, Applied=true</c> for a schedule that did not exist afterwards
    ///     (reproduced on dev 2026-07-28) — and the report is the only evidence an operator has
    ///     that a migration landed.
    /// </summary>
    public ConfigImportReportDto ToReport(bool dryRun, bool requiresApproval, bool applied,
        IReadOnlyList<ConfigImportItemDto>? resolvedItems = null)
    {
        var rows = resolvedItems ?? Items.Select(i => i.Report).ToList();
        bool Failed(ConfigImportItemDto r) => r.Outcome == ConfigImportOutcomes.Failed;

        return new ConfigImportReportDto(
            dryRun, requiresApproval, applied,
            rows.Count(r => r.Action == ConfigImportActions.Create && !Failed(r)),
            rows.Count(r => r.Action == ConfigImportActions.Update && !Failed(r)),
            rows.Count(r => r.Action == ConfigImportActions.Unchanged && !Failed(r)),
            rows.Count(r => r.Action == ConfigImportActions.Error || Failed(r)),
            rows);
    }
}
