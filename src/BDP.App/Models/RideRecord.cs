using SQLite;

namespace BDP.App.Models;

public sealed class RideRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public double DistanceMeters { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>JSON-serialized List&lt;TrackPoint&gt;.</summary>
    public string TrackPointsJson { get; set; } = string.Empty;

    public bool IsUploaded { get; set; }
}
