using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

/// <summary>
///     The delivery-writer prompt carries a conditional block that forbids audio tags outright when
///     the bulletin's voice cannot interpret them. A non-v3 voice SPEAKS a tag, so if that block
///     silently failed to render - a falsy-bool bug in the template engine, a renamed key, a typo in
///     the <c>{{#if}}</c> - the writer would go on being told to use "at least two tags" for a voice
///     that reads them aloud. The output is stripped downstream either way, but then the operator
///     asks for energy and gets a rewrite that spent its instructions on tags nobody will hear.
/// </summary>
public sealed class DeliveryWriterPromptTests
{
    private const string TagBlockMarker = "CANNOT USE TAGS";

    private static string Render(bool noTags)
    {
        return HandlebarsTemplateRenderer.Render(PromptLibrary.DeliveryWriterTemplate,
            new Dictionary<string, object?>
            {
                ["script"] = "The Monash Freeway is closed citybound at Toorak Road.",
                ["intent"] = "Talk about this more calmly as this is regarding a death.",
                ["noTags"] = noTags,
            });
    }

    [Fact]
    public void NoTagsBlock_Renders_WhenTheVoiceCannotInterpretTags()
    {
        string prompt = Render(true);

        prompt.ShouldContain(TagBlockMarker);
        prompt.ShouldContain("Use ZERO tags");
        // The rewrite must still be worth doing: the measured structural levers all work on v2.
        prompt.ShouldContain("split long sentences");
    }

    [Fact]
    public void NoTagsBlock_IsAbsent_OnAV3Voice()
    {
        Render(false).ShouldNotContain(TagBlockMarker);
    }

    [Fact]
    public void BothRenders_KeepTheScriptAndIntentAndLeaveNoUnrenderedPlaceholders()
    {
        foreach (bool noTags in (bool[])[true, false])
        {
            string prompt = Render(noTags);

            prompt.ShouldContain("Monash Freeway");
            prompt.ShouldContain("regarding a death");
            prompt.ShouldNotContain("{{");
            prompt.ShouldNotContain("noTags");
        }
    }
}
