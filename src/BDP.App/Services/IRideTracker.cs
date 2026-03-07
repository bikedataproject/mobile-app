using BDP.App.Models;

namespace BDP.App.Services;

public enum RideState { Idle, Recording, Paused, Stopped }

public interface IRideTracker
{
    RideState State { get; }
    IReadOnlyList<TrackPoint> Points { get; }
    double DistanceMeters { get; }
    TimeSpan Duration { get; }
    double CurrentSpeedKmh { get; }

    string LastGpsStatus { get; }
    int RawReadingsCount { get; }
    int FilteredOutCount { get; }

    event Action? StateChanged;
    event Action<TrackPoint>? PointAdded;

    Task StartAsync();
    void Pause();
    Task ResumeAsync();
    Task<RideRecord> StopAsync();
}
