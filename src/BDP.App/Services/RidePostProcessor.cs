using BDP.App.Models;

namespace BDP.App.Services;

public sealed class RidePostProcessor
{
    private readonly PreferencesService _prefs;

    public RidePostProcessor(PreferencesService prefs)
    {
        _prefs = prefs;
    }

    /// <summary>
    /// Validates and trims a ride. Returns null if the ride doesn't meet quality thresholds.
    /// </summary>
    public RideRecord? Process(RideRecord ride, List<TrackPoint> points)
    {
        if (points.Count < 10) return null;

        // Trim leading/trailing stationary points
        var trimmed = TrimStationary(points);
        if (trimmed.Count < 10) return null;

        var totalDistance = CalculateTotalDistance(trimmed);
        var duration = trimmed[^1].Timestamp - trimmed[0].Timestamp;

        // Minimum distance
        if (totalDistance < _prefs.MinRideDistanceMeters) return null;

        // Minimum duration: 60 seconds
        if (duration.TotalSeconds < 60) return null;

        // Average speed 3-60 km/h
        var avgSpeedKmh = (totalDistance / duration.TotalSeconds) * 3.6;
        if (avgSpeedKmh < 3.0 || avgSpeedKmh > 60.0) return null;

        // No single gap > 5 minutes
        for (int i = 1; i < trimmed.Count; i++)
        {
            var gap = trimmed[i].Timestamp - trimmed[i - 1].Timestamp;
            if (gap.TotalMinutes > 5) return null;
        }

        ride.DistanceMeters = totalDistance;
        ride.Duration = duration;
        ride.StartTime = trimmed[0].Timestamp;
        ride.EndTime = trimmed[^1].Timestamp;
        ride.TrackPointsJson = System.Text.Json.JsonSerializer.Serialize(trimmed);

        return ride;
    }

    private static List<TrackPoint> TrimStationary(List<TrackPoint> points)
    {
        int start = 0;
        int end = points.Count - 1;

        // Trim leading stationary points (speed < 1 km/h between consecutive points)
        while (start < end - 1)
        {
            var dist = HaversineDistance(
                points[start].Latitude, points[start].Longitude,
                points[start + 1].Latitude, points[start + 1].Longitude);
            var seconds = (points[start + 1].Timestamp - points[start].Timestamp).TotalSeconds;
            if (seconds > 0 && (dist / seconds) * 3.6 >= 1.0) break;
            start++;
        }

        // Trim trailing stationary points
        while (end > start + 1)
        {
            var dist = HaversineDistance(
                points[end - 1].Latitude, points[end - 1].Longitude,
                points[end].Latitude, points[end].Longitude);
            var seconds = (points[end].Timestamp - points[end - 1].Timestamp).TotalSeconds;
            if (seconds > 0 && (dist / seconds) * 3.6 >= 1.0) break;
            end--;
        }

        return points.GetRange(start, end - start + 1);
    }

    private static double CalculateTotalDistance(List<TrackPoint> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += HaversineDistance(
                points[i - 1].Latitude, points[i - 1].Longitude,
                points[i].Latitude, points[i].Longitude);
        }
        return total;
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
