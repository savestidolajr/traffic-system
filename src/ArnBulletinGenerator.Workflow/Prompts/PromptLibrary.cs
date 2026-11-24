using System.Collections.Concurrent;

namespace ArnBulletinGenerator.Workflow.Prompts;

/// <summary>
///     Loads role prompts and response schemas from the application output directory, cached per
///     process.
///     <para>
///         Prompts are files, not string literals, for the same reason the deployed pipeline keeps
///         them in <c>prompts/</c>: an editorial change has to be reviewable as a diff by someone who
///         does not read C#. Schemas sit next to them so a contract change and the instruction that
///         explains it move together.
///     </para>
/// </summary>
public static class PromptLibrary
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Root the prompt and schema folders are resolved against. Overridable for tests.</summary>
    public static string Root { get; set; } = AppContext.BaseDirectory;
    /// <summary>The standing instructions for a role, e.g. <c>compose2</c> loads <c>Prompts/compose2.md</c>.</summary>
    public static string System(string role) => Load(Path.Combine("Prompts", $"{role}.md"));

    /// <summary>The JSON response schema for a role, e.g. <c>compose2</c> loads <c>Schemas/compose2.json</c>.</summary>
    public static string Schema(string role) => Load(Path.Combine("Schemas", $"{role}.json"));

    private static string Load(string relativePath) =>
        Cache.GetOrAdd(relativePath, key =>
        {
            string full = Path.Combine(Root, key);
            return File.Exists(full)
                ? File.ReadAllText(full)
                : throw new FileNotFoundException($"Workflow prompt asset not found: {full}", full);
        });

    /// <summary>Drops every cached asset. Tests that rewrite prompts on disk must call this.</summary>
    public static void Reset() => Cache.Clear();
}
