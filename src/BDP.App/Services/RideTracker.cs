using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

public sealed class RideTracker : IRideTracker
{
    private const double MaxAccuracyMeters = 30.0;
    private const double MaxSpeedKmh = 150.0;
    private const double MinDistanceBetweenPointsMeters = 2.0;

    private readonly ILocationService _locationService;
    private readonly List<TrackPoint> _points = [];
    private DateTimeOffset _startTime;
    private DateTimeOffset _pauseTime;
    private TimeSpan _accumulatedDuration;

    public RideState State { get; private set; } = RideState.Idle;
    public IReadOnlyList<TrackPoint> Points => _points;
    public double DistanceMeters { get; private set; }
    public TimeSpan Duration => State == RideState.Recording
        ? _accumulatedDuration + (DateTimeOffset.UtcNow - _startTime)
        : _accumulatedDuration;
    public double CurrentSpeedKmh { get; private set; }

    public event Action? StateChanged;
    public event Action<TrackPoint>? PointAdded;

    public RideTracker(ILocationService locationService)
    {
        _locationService = locationService;
    }

    public async Task StartAsync()
    {
        _points.Clear();
        DistanceMeters = 0;
        _accumulatedDuration = TimeSpan.Zero;
        CurrentSpeedKmh = 0;

        _locationService.LocationUpdated += OnLocationUpdated;
        var started = await _locationService.StartAsync();
        if (!started) return;

        _startTime = DateTimeOffset.UtcNow;
        State = RideState.Recording;
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (State != RideState.Recording) return;

        _accumulatedDuration += DateTimeOffset.UtcNow - _startTime;
        _pauseTime = DateTimeOffset.UtcNow;
        State = RideState.Paused;
        StateChanged?.Invoke();
    }

    public async Task ResumeAsync()
    {
        if (State != RideState.Paused) return;

        _startTime = DateTimeOffset.UtcNow;
        State = RideState.Recording;
        StateChanged?.Invoke();

        if (!_locationService.IsTracking)
            await _locationService.StartAsync();
    }

    public async Task<RideRecord> StopAsync()
    {
        if (State == RideState.Recording)
            _accumulatedDuration += DateTimeOffset.UtcNow - _startTime;

        await _locationService.StopAsync();
        _locationService.LocationUpdated -= OnLocationUpdated;

        State = RideState.Stopped;
        StateChanged?.Invoke();

        var ride = new RideRecord
        {
            StartTime = _points.Count > 0 ? _points[0].Timestamp : _startTime,
            EndTime = _points.Count > 0 ? _points[^1].Timestamp : DateTimeOffset.UtcNow,
            DistanceMeters = DistanceMeters,
            Duration = _accumulatedDuration,
            TrackPointsJson = JsonSerializer.Serialize(_points),
            IsUploaded = false
        };

        State = RideState.Idle;
        return ride;
    }

    private void OnLocationUpdated(TrackPoint point)
    {
        if (State != RideState.Recording) return;

        // Filter: accuracy
        if (point.Accuracy > MaxAccuracyMeters) return;

        // Filter: timestamp must be increasing
        if (_points.Count > 0 && point.Timestamp <= _points[^1].Timestamp) return;

        if (_points.Count > 0)
        {
            var last = _points[^1];
            var dist = HaversineDistance(last.Latitude, last.Longitude, point.Latitude, point.Longitude);

            // Filter: minimum distance
            if (dist < MinDistanceBetweenPointsMeters) return;

            // Filter: implied speed
            var seconds = (point.Timestamp - last.Timestamp).TotalSeconds;
            if (seconds > 0)
            {
                var impliedSpeedKmh = (dist / seconds) * 3.6;
                if (impliedSpeedKmh > MaxSpeedKmh) return;

                CurrentSpeedKmh = impliedSpeedKmh;
            }

            DistanceMeters += dist;
        }

        _points.Add(point);
        PointAdded?.Invoke(point);
    }

    internal static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
