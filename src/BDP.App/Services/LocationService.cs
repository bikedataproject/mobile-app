using BDP.App.Models;

namespace BDP.App.Services;

public sealed class LocationService : ILocationService
{
    public bool IsTracking { get; private set; }
    public string LastStatus { get; private set; } = "Not started";
    public event Action<TrackPoint>? LocationUpdated;

    public async Task<bool> StartAsync()
    {
        LastStatus = "Checking permissions...";

        // Request WhenInUse first (required before requesting Always on both platforms)
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                LastStatus = $"Permission denied: {status}";
                return false;
            }
        }

        // Request Always for background tracking
        var alwaysStatus = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
        if (alwaysStatus != PermissionStatus.Granted)
        {
            alwaysStatus = await Permissions.RequestAsync<Permissions.LocationAlways>();
            LastStatus = $"Background location: {alwaysStatus}";
            // Continue even if denied — foreground tracking still works
        }

        LastStatus = "Permission granted. Starting GPS...";

        StartPlatformForegroundService();

        // Use the continuous location listener — this actively powers on the GPS hardware
        Geolocation.Default.LocationChanged += OnLocationChanged;
        var listening = await Geolocation.Default.StartListeningForegroundAsync(new GeolocationListeningRequest
        {
            DesiredAccuracy = GeolocationAccuracy.Best,
            MinimumTime = TimeSpan.FromSeconds(1)
        });

        if (!listening)
        {
            LastStatus = "Failed to start location listener";
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            StopPlatformForegroundService();
            return false;
        }

        IsTracking = true;
        LastStatus = "GPS active, waiting for fix...";
        return true;
    }

    public Task StopAsync()
    {
        Geolocation.Default.StopListeningForeground();
        Geolocation.Default.LocationChanged -= OnLocationChanged;

        IsTracking = false;
        StopPlatformForegroundService();

        LastStatus = "Stopped";
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var location = e.Location;
        LastStatus = $"GPS: {location.Latitude:F5},{location.Longitude:F5} acc:{location.Accuracy:F0}m";

        var point = new TrackPoint
        {
            Longitude = location.Longitude,
            Latitude = location.Latitude,
            Elevation = location.Altitude,
            Timestamp = location.Timestamp != default ? location.Timestamp : DateTimeOffset.UtcNow,
            Accuracy = location.Accuracy ?? double.MaxValue,
            Speed = location.Speed
        };

        LocationUpdated?.Invoke(point);
    }

    private static void StartPlatformForegroundService()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(Platforms.Android.Services.LocationForegroundService));
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
#endif
    }

    private static void StopPlatformForegroundService()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(Platforms.Android.Services.LocationForegroundService));
        context.StopService(intent);
#endif
    }
}
