using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Global delivery-direction settings. The script director adds ElevenLabs v3 audio tags to
///     steer how a bulletin is read. A station's own delivery style (resolved by the poller) always
///     wins; this is the fallback applied to bulletins with no per-station style assigned.
///
///     Disabled by default and neutral by default, so out of the box every bulletin reads exactly as
///     it does today. Set <see cref="Enabled" /> with a non-empty <see cref="DefaultAllowedTags" />
///     to turn on a global "house" delivery (and to exercise the director in eval).
/// </summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>Master switch for the config-default policy. When false the default policy is neutral.</summary>
    public bool Enabled { get; set; }

    /// <summary>Plain-language tone for the default house delivery.</summary>
    public string DefaultTone { get; set; } = "Neutral, even, professional Australian radio read.";

    /// <summary>Allowed ElevenLabs v3 audio tags (with brackets) the director may use by default.</summary>
    public List<string> DefaultAllowedTags { get; set; } = [];

    /// <summary>Default tagging intensity (0 none … 3 heavy).</summary>
    public int DefaultIntensity { get; set; }

    /// <summary>
    ///     The config-default policy, or <see cref="DeliveryStylePolicy.Neutral" /> when disabled or
    ///     when no tags are configured (which keeps the director a no-op).
    /// </summary>
    public DeliveryStylePolicy DefaultPolicy =>
        Enabled && DefaultAllowedTags.Count > 0
            ? new DeliveryStylePolicy(DefaultTone, DefaultAllowedTags, DefaultIntensity)
            : DeliveryStylePolicy.Neutral;
}
