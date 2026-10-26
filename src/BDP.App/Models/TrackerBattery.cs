namespace BDP.App.Models;

/// <summary>
/// Battery state read from the tracker's battery characteristic.
/// Only produced when the device reports a divider is fitted (avail=1).
/// </summary>
public sealed class TrackerBattery
{
    public int Millivolts { get; init; }
    public int Percent { get; init; }

    /// <summary>Below 20%. Advisory only — the device still tracks.</summary>
    public bool IsLow { get; init; }

    /// <summary>Below 10%. The device has stopped starting new rides to preserve what it holds.</summary>
    public bool IsCritical { get; init; }
}
