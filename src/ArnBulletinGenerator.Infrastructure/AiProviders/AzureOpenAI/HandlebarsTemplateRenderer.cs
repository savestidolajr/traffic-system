using System.Collections.Concurrent;
using HandlebarsDotNet;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Compiles and renders the prompts/*.md Handlebars templates directly against
///     <c>Handlebars.Net</c> — the same underlying library Semantic Kernel's
///     <c>HandlebarsPromptTemplateFactory</c> wrapped, used here without the SK <c>Kernel</c>
///     object that Arn's LLM calls never actually needed (no connectors, no plugins).
/// </summary>
/// <remarks>
///     <see cref="HandlebarsConfiguration.TextEncoder" /> is set to <see langword="null" /> to
///     disable Handlebars' default HTML entity-encoding of <c>{{double-stache}}</c> values —
///     these are plain-text LLM prompts, not HTML. Without this, any road/suburb/detail text
///     containing <c>&amp;</c> or <c>"</c> (e.g. a quoted "Source detail" cause like
///     <c>"ASICS Gold Coast Marathon 2026"</c>) would be corrupted in the rendered prompt
///     (verified: default encoder turns <c>&amp;</c> into <c>&amp;amp;</c>, <c>"</c> into
///     <c>&amp;quot;</c>). <c>{{{triple-stache}}}</c> values (traffic/style/guidance blocks) are
///     never encoded either way, matching prior SK behaviour.
/// </remarks>
internal static class HandlebarsTemplateRenderer
{
    private static readonly IHandlebars Environment = HandlebarsDotNet.Handlebars.Create(
        new HandlebarsConfiguration { TextEncoder = null });

    private static readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> Compiled = new();

    /// <summary>Compiles (once, cached) and renders <paramref name="template" /> against <paramref name="arguments" />.</summary>
    public static string Render(string template, IDictionary<string, object?> arguments)
    {
        var compiled = Compiled.GetOrAdd(template, Environment.Compile);
        return compiled(arguments);
    }
}
