using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using ArnBulletinGenerator.Workflow.Contracts;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Conversational;

/// <summary>
///     The station playbook must reach the conversational composer INDEPENDENTLY of the structured
///     voice/editorial settings. A station whose only configuration is a playbook edit previously
///     mapped to <see cref="StationPolicy.Empty" /> and had that edit silently dropped — the same
///     dead-wire failure class as the unrendered future lessons.
/// </summary>
public sealed class ConversationalStationPolicyTests
{
    private static BroadcastWithTraffic Context(PlaybookOverlay? playbook = null) => new()
    {
        JobId = "policy-mapping-test",
        Location = "Sydney",
        TargetDurationSeconds = 30,
        Traffic = "ignored",
        Incidents = [],
        Playbook = playbook
    };
    [Fact]
    public void A_playbook_only_station_keeps_its_playbook()
    {
        var overlay = new PlaybookOverlay
        {
            Disable = ["voice.variety"]
        };

        StationPolicy policy = ConversationalScriptGenerator.StationPolicyFor(Context(overlay));

        policy.PlaybookFor("compose2").ShouldBeSameAs(overlay);
    }

    [Fact]
    public void An_unconfigured_station_still_maps_to_the_empty_policy()
    {
        StationPolicy policy = ConversationalScriptGenerator.StationPolicyFor(Context());

        policy.ShouldBeSameAs(StationPolicy.Empty);
    }


}
