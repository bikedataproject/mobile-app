using SQLite;

namespace BDP.App.Models;

public sealed class PairedTracker
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string DeviceName { get; set; } = "BikeTracker";
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public int TotalPointsSynced { get; set; }
    public int TotalRidesSynced { get; set; }
    public int LastSyncedIndex { get; set; }
    public string BufferedPointsJson { get; set; } = "[]";
}
