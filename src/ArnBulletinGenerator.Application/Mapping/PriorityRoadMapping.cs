using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>Wire↔domain mapping for <see cref="FlowPriorityTier" />.</summary>
public static class PriorityRoadMapping
{
    public const string ValidFlowTierValues = "none, normal, high, critical";

    public static bool TryWireToTier(string? wire, out FlowPriorityTier tier)
    {
        switch (wire?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
                tier = FlowPriorityTier.None;
                return true;
            case "none":
                tier = FlowPriorityTier.None;
                return true;
            case "normal":
                tier = FlowPriorityTier.Normal;
                return true;
            case "high":
                tier = FlowPriorityTier.High;
                return true;
            case "critical":
                tier = FlowPriorityTier.Critical;
                return true;
            default:
                tier = FlowPriorityTier.None;
                return false;
        }
    }

    public static FlowPriorityTier WireToTier(string? wire) =>
        TryWireToTier(wire, out var tier) ? tier : FlowPriorityTier.Normal;

    public static string InvalidFlowTierMessage(string wire) =>
        $"FlowTier '{wire}' is not recognised. Valid values: {ValidFlowTierValues}.";
}
