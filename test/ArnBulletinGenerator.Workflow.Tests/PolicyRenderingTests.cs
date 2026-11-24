using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Prompts;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     Every station setting must reach the model, and no unset setting may.
///     <para>
///         Both directions have failed in practice. <c>MaxDelayFigures</c> was declared, counted in
///         <c>IsEmpty</c>, and rendered nowhere: an operator could set ARN's own house position of
///         one delay figure per read and nothing would happen, which is worse than not offering the
///         setting at all. In the other direction <c>MaxSentenceWords</c> defaulted to 24 and
///         rendered as soon as ANY unrelated voice field was set, so switching on a house style that
///         asked for varied sentence lengths capped them in the same breath - measured live on
///         2026-08-26 across five markets, every sentence over 24 words disappeared.
///     </para>
/// </summary>
public sealed class PolicyRenderingTests
{
    [Fact]
    public void An_empty_voice_policy_renders_nothing()
    {
        PolicyRenderer.ForComposer(VoicePolicy.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Setting_one_field_does_not_switch_on_an_unset_ceiling()
    {
        string rendered = PolicyRenderer.ForComposer(new VoicePolicy { Tone = "brisk and factual" });

        rendered.ShouldContain("brisk and factual");
        rendered.ShouldNotContain("spoken words", Case.Sensitive);
        rendered.ShouldNotContain("delay figure", Case.Sensitive);
    }

    [Theory]
    [InlineData("Tone", "warm breakfast")]
    [InlineData("RoadFeelPalette", "Heavy: chockers")]
    [InlineData("PreferredPhrases", "under speed")]
    [InlineData("AvoidPhrases", "at a standstill")]
    [InlineData("Examples", "The M1 is shut southbound")]
    [InlineData("Notes", "lead with the closure")]
    public void Every_authored_setting_reaches_the_composer(string field, string value)
    {
        VoicePolicy policy = field switch
        {
            "Tone" => new VoicePolicy { Tone = value },
            "RoadFeelPalette" => new VoicePolicy { RoadFeelPalette = value },
            "PreferredPhrases" => new VoicePolicy { PreferredPhrases = [value] },
            "AvoidPhrases" => new VoicePolicy { AvoidPhrases = [value] },
            "Examples" => new VoicePolicy { Examples = [value] },
            "Notes" => new VoicePolicy { Notes = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "unmapped setting")
        };

        PolicyRenderer.ForComposer(policy).ShouldContain(value, Case.Sensitive,
            $"{field} is settable but never reaches the model, so an operator changing it sees nothing happen");
    }

    [Fact]
    public void A_sentence_ceiling_reaches_the_composer_when_chosen()
    {
        PolicyRenderer.ForComposer(new VoicePolicy { MaxSentenceWords = 18 })
            .ShouldContain("18 spoken words");
    }

    [Fact]
    public void A_delay_figure_budget_reaches_the_composer_when_chosen()
    {
        // The knob this test exists for: declared, counted in IsEmpty, and rendered nowhere.
        PolicyRenderer.ForComposer(new VoicePolicy { MaxDelayFigures = 1 })
            .ShouldContain("at most 1 delay figure");
    }

    [Fact]
    public void Voice_settings_are_told_they_cannot_drop_a_fact()
    {
        // A wording preference that could delete content would hand editorial authority to the
        // wrong stage, so the boundary is restated every time any voice policy is rendered.
        PolicyRenderer.ForComposer(new VoicePolicy { Tone = "brisk" })
            .ShouldContain("never licenses dropping a fact");
    }
}
