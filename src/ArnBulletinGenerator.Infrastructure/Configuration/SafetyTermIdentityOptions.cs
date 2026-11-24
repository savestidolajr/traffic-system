namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>Controls the additive SafetyTerm identity index rollout.</summary>
public sealed class SafetyTermIdentityOptions
{
    public bool WriteEnabled { get; init; } = true;
    public bool ReadEnabled { get; init; } = true;
    public bool KillSwitch { get; init; }
}
