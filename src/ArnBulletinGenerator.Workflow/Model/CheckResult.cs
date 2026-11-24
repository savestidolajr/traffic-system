namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>One deterministic check's verdict over a composed read.</summary>
/// <param name="Name">Check name, shown in the viewer.</param>
/// <param name="Passed">False routes the read to repair.</param>
/// <param name="Detail">Why it failed, or what it measured when it passed.</param>
/// <param name="SentenceOrder">The sentence at fault, when the check is sentence-scoped.</param>
public sealed record CheckResult(string Name, bool Passed, string Detail, int? SentenceOrder = null);
