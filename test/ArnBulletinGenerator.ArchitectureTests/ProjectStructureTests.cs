using System.Text.RegularExpressions;
using Shouldly;

namespace ArnBulletinGenerator.ArchitectureTests;

/// <summary>
/// File-system-based convention rules for the Arn bulletin generator's domain-folder
/// structure (NetArchTest operates on compiled types and cannot see file paths, so these
/// rules walk the source tree directly).
///
/// Conventions enforced here come from
/// docs/canonical/decisions.md, 2026-07-21 "Backend domain-folder structure and
/// collaborator-split convention": Application and Functions are organized into domain
/// folders whose names match their namespaces; new files go into the matching domain
/// folder, not the project root.
/// </summary>
public sealed class ProjectStructureTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ApplicationRoot = Path.Combine(RepoRoot, "src", "ArnBulletinGenerator.Application");
    private static readonly string FunctionsRoot = Path.Combine(RepoRoot, "src", "ArnBulletinGenerator.Functions");

    private static readonly Regex NamespaceDeclaration =
        new(@"^\s*namespace\s+([A-Za-z0-9_.]+)\s*[;{]", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Program.cs is a top-level-statement entry point: it has no namespace declaration by
    /// design. It is covered separately by <see cref="Functions_RootLevel_Files_AreAllowlisted"/>
    /// and is excluded here rather than from the namespace-matches-folder rule.
    /// </summary>
    private const string FunctionsEntryPointFile = "Program.cs";

    // ======================================================================
    // Rule 1 & 2: no stray root-level .cs files
    // ======================================================================

    [Fact]
    public void Application_RootLevel_Files_AreAllowlisted()
    {
        var allowed = new HashSet<string> { "DependencyInjection.cs" };

        var stray = Directory.EnumerateFiles(ApplicationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !allowed.Contains(name!))
            .ToList();

        stray.ShouldBeEmpty(
            $"Found stray root-level .cs file(s) directly in src/ArnBulletinGenerator.Application/: " +
            $"{string.Join(", ", stray)}. Move each into the matching domain folder " +
            "(Broadcasting/, Scheduling/, Jobs/, Libraries/, ConfigTransfer/, Access/, Metrics/, " +
            "Commands/, Mapping/, ConfigImport/, Validation/, or a new one) and set its namespace to " +
            "ArnBulletinGenerator.Application.<FolderName> — see docs/canonical/decisions.md, " +
            "2026-07-21 'Backend domain-folder structure and collaborator-split convention'.");
    }

    [Fact]
    public void Functions_RootLevel_Files_AreAllowlisted()
    {
        var allowed = new HashSet<string> { FunctionsEntryPointFile };

        var stray = Directory.EnumerateFiles(FunctionsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !allowed.Contains(name!))
            .ToList();

        stray.ShouldBeEmpty(
            $"Found stray root-level .cs file(s) directly in src/ArnBulletinGenerator.Functions/: " +
            $"{string.Join(", ", stray)}. Move each into the matching folder " +
            "(Triggers/Http/, Triggers/Queues/, Triggers/Timers/, Middleware/, Auth/, Http/, Metrics/, " +
            "ConfigTransfer/, Contracts/, or a new one) and set its namespace to " +
            "ArnBulletinGenerator.Functions.<FolderName> — see docs/canonical/decisions.md, " +
            "2026-07-21 'Backend domain-folder structure and collaborator-split convention'.");
    }

    // ======================================================================
    // Rule 3 & 4: namespace matches folder path
    // ======================================================================

    [Fact]
    public void Application_Namespaces_MatchFolderPath()
    {
        var violations = FindNamespaceFolderViolations(ApplicationRoot, "ArnBulletinGenerator.Application", excludedFiles: []);

        violations.ShouldBeEmpty(
            "Namespace/folder mismatch(es) in ArnBulletinGenerator.Application:\n" +
            string.Join("\n", violations) +
            "\nEach file's namespace must equal ArnBulletinGenerator.Application[.<folder path, '.'-separated>]. " +
            "Either move the file to the folder implied by its namespace, or fix the namespace declaration to " +
            "match its folder — see docs/canonical/decisions.md, 2026-07-21 " +
            "'Backend domain-folder structure and collaborator-split convention'.");
    }

    [Fact]
    public void Functions_Namespaces_MatchFolderPath()
    {
        var violations = FindNamespaceFolderViolations(
            FunctionsRoot,
            "ArnBulletinGenerator.Functions",
            excludedFiles: [FunctionsEntryPointFile]);

        violations.ShouldBeEmpty(
            "Namespace/folder mismatch(es) in ArnBulletinGenerator.Functions:\n" +
            string.Join("\n", violations) +
            "\nEach file's namespace must equal ArnBulletinGenerator.Functions[.<folder path, '.'-separated>]. " +
            "Either move the file to the folder implied by its namespace, or fix the namespace declaration to " +
            "match its folder — see docs/canonical/decisions.md, 2026-07-21 " +
            "'Backend domain-folder structure and collaborator-split convention'.");
    }

    // ======================================================================
    // Rule 5: Function-triggered entry points live under Triggers/
    // ======================================================================

    [Fact]
    public void FunctionAttributes_OnlyAppear_UnderTriggersFolder()
    {
        var triggersRoot = Path.Combine(FunctionsRoot, "Triggers") + Path.DirectorySeparatorChar;

        var offenders = EnumerateSourceFiles(FunctionsRoot)
            .Where(file => File.ReadAllText(file).Contains("[Function(", StringComparison.Ordinal))
            .Where(file => !file.StartsWith(triggersRoot, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .ToList();

        offenders.ShouldBeEmpty(
            $"Found [Function(...)]-attributed method(s) outside Triggers/: {string.Join(", ", offenders)}. " +
            "Azure Functions entry points (HTTP/queue/timer triggers) must live under " +
            "src/ArnBulletinGenerator.Functions/Triggers/{Http,Queues,Timers}/ — move the trigger class there " +
            "and set its namespace to ArnBulletinGenerator.Functions.Triggers.<Http|Queues|Timers> — see " +
            "docs/canonical/decisions.md, 2026-07-21 'Backend domain-folder structure and collaborator-split " +
            "convention'.");
    }

    // ======================================================================
    // Helpers
    // ======================================================================

    private static List<string> FindNamespaceFolderViolations(
        string projectRoot,
        string projectNamespace,
        IReadOnlyCollection<string> excludedFiles)
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(projectRoot))
        {
            var fileName = Path.GetFileName(file);
            if (excludedFiles.Contains(fileName))
                continue;

            var relativeDir = Path.GetRelativePath(projectRoot, Path.GetDirectoryName(file)!);
            var expectedNamespace = relativeDir == "."
                ? projectNamespace
                : $"{projectNamespace}.{relativeDir.Replace(Path.DirectorySeparatorChar, '.')}";

            var match = NamespaceDeclaration.Match(File.ReadAllText(file));
            var relativePath = Path.GetRelativePath(RepoRoot, file);

            if (!match.Success)
            {
                violations.Add(
                    $"  {relativePath}: no `namespace X;` declaration found — add " +
                    $"`namespace {expectedNamespace};` to match its folder.");
                continue;
            }

            var actualNamespace = match.Groups[1].Value;
            if (actualNamespace != expectedNamespace)
            {
                violations.Add(
                    $"  {relativePath}: namespace is `{actualNamespace}`, expected `{expectedNamespace}` " +
                    $"for its folder.");
            }
        }

        return violations;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderExcludedDirectory(root, file));

    private static bool IsUnderExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
        var segments = relative.Split(Path.DirectorySeparatorChar);
        return segments.Any(segment => segment is "obj" or "bin");
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
