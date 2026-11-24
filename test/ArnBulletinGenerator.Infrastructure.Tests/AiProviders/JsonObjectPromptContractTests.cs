using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

/// <summary>
///     Guards a hard Azure Responses API constraint that is invisible until it fires in production.
///     <para>
///         An agent configured with <c>text.format</c> of type <c>json_object</c> REJECTS any request
///         whose INPUT MESSAGES do not contain the literal word "json":
///         <c>"Response input messages must contain the word 'json' in some form to use 'text.format'
///         of type 'json_object'."</c>
///     </para>
///     <para>
///         This bit live on 2026-08-20. Moving the refine agent's invariant instructions server-side
///         (to recover prompt caching) also moved the only mention of "json" out of the user message,
///         so every Refine call began failing with that error while the pipeline logged only
///         "Using current script". The reviewer's feedback was therefore never applied: dev job
///         4535f572 ran four attempts and repeated the same REQUIRED SOURCE MISSING complaint each
///         time, and the captures showed Refine returning in ~240ms with zero token usage.
///     </para>
///     <para>
///         The failure mode is silent and cheap-looking, which is exactly why it needs a test rather
///         than a comment: a Refine that never runs looks like a fast Refine.
///     </para>
/// </summary>
public sealed class JsonObjectPromptContractTests
{
    // Only templates the code actually SENDS. draft.md/citation-fix.md/attribute.md left this list
    // on 2026-08-24: attribute.md was deleted with the LLM attributor, and the other two are no
    // longer read by any code path (see prompts/README.md).
    public static TheoryData<string> JsonObjectTemplates() =>
    [
        "review.md", "refine-sentence.md",
        "safety.md", "director.md", "delivery-writer.md"
    ];

    [Theory]
    [MemberData(nameof(JsonObjectTemplates))]
    public void Json_object_templates_mention_json_in_the_user_message(string template)
    {
        string path = Path.Combine(RepoRoot(), "prompts", template);
        File.Exists(path).ShouldBeTrue($"expected prompt template at {path}");

        string body = File.ReadAllText(path);

        body.Contains("json", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            $"prompts/{template} feeds an agent provisioned with format=json_object, so the rendered "
            + "USER message must contain the literal word \"json\" or Azure rejects the request "
            + "outright. Moving the instruction that mentioned it server-side is what broke Refine on "
            + "2026-08-20 — if you have relocated that text, leave a short line here that names the "
            + "JSON reply, e.g. \"Reply with the JSON envelope defined in your instructions.\"");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArnBulletinGenerator.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
