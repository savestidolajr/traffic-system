namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>Controls the additive job work index read/write path.</summary>
public sealed record JobWorkIndexOptions
{
    public const string SectionName = "Jobs:WorkIndex";

    /// <summary>Enables maintaining work-index rows after job writes.</summary>
    public bool WriteEnabled { get; init; }

    /// <summary>Enables watchdog reads from the work index.</summary>
    public bool ReadEnabled { get; init; }

    /// <summary>Disables both work-index reads and writes regardless of the other flags.</summary>
    public bool KillSwitch { get; init; }

    /// <summary>Runs legacy and indexed candidate selection together and logs differences.</summary>
    public bool CompareEnabled { get; init; }

    /// <summary>How far behind the current tick indexed sweeps may read.</summary>
    public int LookbackMinutes { get; init; } = 15;

    /// <summary>How far ahead the current tick indexed sweeps may read.</summary>
    public int HorizonMinutes { get; init; } = 15;

    /// <summary>Maximum rows returned per indexed partition page.</summary>
    public int PageSize { get; init; } = 500;

    /// <summary>Maximum time a watchdog tick spends reading indexed candidates.</summary>
    public int TickBudgetSeconds { get; init; } = 20;
}
