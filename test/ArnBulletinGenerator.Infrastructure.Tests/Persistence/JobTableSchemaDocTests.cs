using System.Reflection;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Drift gate between <see cref="JobEntity"/>'s public properties and
///     docs/reference/job-table-schema.md, which the Python harvest/eval tooling relies on as
///     the non-C# contract for the <c>radioJobs</c> table columns. Reflects over the live
///     properties so a renamed/added/removed column fails loudly here instead of silently
///     rotting the doc.
/// </summary>
public sealed class JobTableSchemaDocTests
{
    private static readonly Regex ColumnCell = new(@"^\|\s*`([A-Za-z0-9]+)`\s*\|", RegexOptions.Multiline);

    [Fact]
    public void JobTableSchemaDoc_ListsExactlyTheCurrentJobEntityColumns()
    {
        var repoRoot = FindRepoRoot();
        var docPath = Path.Combine(repoRoot, "docs", "reference", "job-table-schema.md");
        File.Exists(docPath).ShouldBeTrue($"Expected schema doc at {docPath}");
        var docText = File.ReadAllText(docPath);

        var documentedColumns = ColumnCell.Matches(docText)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var actualColumns = typeof(JobEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var undocumented = actualColumns.Except(documentedColumns).ToList();
        var stale = documentedColumns.Except(actualColumns).ToList();

        undocumented.ShouldBeEmpty(
            "JobEntity changed: update docs/reference/job-table-schema.md (and the Python " +
            $"consumers if the column is one they read). Missing from the doc: {string.Join(", ", undocumented)}");
        stale.ShouldBeEmpty(
            "JobEntity changed: update docs/reference/job-table-schema.md (and the Python " +
            $"consumers if the column is one they read). Doc lists columns that no longer exist: {string.Join(", ", stale)}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArnBulletinGenerator.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing ArnBulletinGenerator.slnx) by walking up " +
            $"from {AppContext.BaseDirectory}.");
    }
}
