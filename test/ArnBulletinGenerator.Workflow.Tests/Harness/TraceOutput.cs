using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Tests.Harness;

/// <summary>
///     Collects every trace produced by a test run and writes them, plus an index, under
///     <c>workflow-traces/</c> at the repository root.
///     <para>
///         This is not optional tooling. The whole reason this workflow is being trialled is that the
///         previous one could not be inspected after the fact: prompts were server-side, captures held
///         placeholders, and judging a change meant reading log lines. Here every test emits the full
///         landscape of its run as a browsable artefact, so a change is evaluated by looking at what
///         actually happened rather than at whether an assertion went green.
///     </para>
/// </summary>
public static class TraceOutput
{
    private static readonly Lock Gate = new();
    private static readonly List<WorkflowTrace> Collected = [];

    /// <summary>Directory every trace is written to. Created on first use.</summary>
    public static string Directory { get; } = Path.Combine(RepositoryRoot(), "workflow-traces");

    /// <summary>Writes one trace and returns its path. Also queues it for the index.</summary>
    public static string Emit(WorkflowTrace trace)
    {
        lock (Gate)
        {
            Collected.Add(trace);
            string path = TraceHtmlRenderer.Write(trace, Directory);
            TraceHtmlRenderer.WriteIndex(Collected.ToList(), Directory);
            return path;
        }
    }

    /// <summary>Every trace emitted so far this process.</summary>
    public static IReadOnlyList<WorkflowTrace> All
    {
        get { lock (Gate) { return Collected.ToList(); } }
    }

    /// <summary>
    ///     Walks up from the test assembly to the folder holding the solution file, so traces land in
    ///     one predictable place no matter which project or runner invoked the test.
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArnBulletinGenerator.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
