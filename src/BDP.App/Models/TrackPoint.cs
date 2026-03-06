namespace BDP.App.Models;

public sealed class TrackPoint
{
    public double Longitude { get; init; }
    public double Latitude { get; init; }
    public double? Elevation { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public double Accuracy { get; init; }
    public double? Speed { get; init; }
}
