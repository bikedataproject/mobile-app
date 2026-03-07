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
    public string LastGpsStatus { get; private set; } = "Waiting for GPS...";
    public int RawReadingsCount { get; private set; }
    public int FilteredOutCount { get; private set; }

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
        RawReadingsCount = 0;
        FilteredOutCount = 0;
        LastGpsStatus = "Waiting for GPS...";

        _locationService.LocationUpdated += OnLocationUpdated;
        var started = await _locationService.StartAsync();
        if (!started)
        {
            LastGpsStatus = $"Location start failed: {_locationService.LastStatus}";
            _locationService.LocationUpdated -= OnLocationUpdated;
            return;
        }

        _startTime = DateTimeOffset.UtcNow;
        State = RideState.Recording;
        LastGpsStatus = _locationService.LastStatus;
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
        StateChanged?.Invoke();
        return ride;
    }

    private void OnLocationUpdated(TrackPoint point)
    {
        if (State != RideState.Recording) return;

        RawReadingsCount++;
        LastGpsStatus = $"GPS: {point.Latitude:F5}, {point.Longitude:F5} acc:{point.Accuracy:F0}m";

        // Filter: accuracy
        if (point.Accuracy > MaxAccuracyMeters)
        {
            FilteredOutCount++;
            LastGpsStatus = $"Filtered: accuracy {point.Accuracy:F0}m > {MaxAccuracyMeters}m";
            return;
        }

        // Filter: timestamp must be increasing
        if (_points.Count > 0 && point.Timestamp <= _points[^1].Timestamp)
        {
            FilteredOutCount++;
            LastGpsStatus = "Filtered: timestamp not increasing";
            return;
        }

        if (_points.Count > 0)
        {
            var last = _points[^1];
            var dist = HaversineDistance(last.Latitude, last.Longitude, point.Latitude, point.Longitude);

            // Filter: minimum distance
            if (dist < MinDistanceBetweenPointsMeters)
            {
                FilteredOutCount++;
                LastGpsStatus = $"Filtered: moved {dist:F1}m < {MinDistanceBetweenPointsMeters}m";
                return;
            }

            // Filter: implied speed
            var seconds = (point.Timestamp - last.Timestamp).TotalSeconds;
            if (seconds > 0)
            {
                var impliedSpeedKmh = (dist / seconds) * 3.6;
                if (impliedSpeedKmh > MaxSpeedKmh)
                {
                    FilteredOutCount++;
                    LastGpsStatus = $"Filtered: speed {impliedSpeedKmh:F0}km/h > {MaxSpeedKmh}km/h";
                    return;
                }

                CurrentSpeedKmh = impliedSpeedKmh;
            }

            DistanceMeters += dist;
        }

        _points.Add(point);
        LastGpsStatus = $"Added #{_points.Count}: {point.Latitude:F5}, {point.Longitude:F5} acc:{point.Accuracy:F0}m";
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
