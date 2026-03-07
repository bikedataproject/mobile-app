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
        Geolocation.Default.ListeningFailed += OnListeningFailed;

        var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
        var listening = await Geolocation.Default.StartListeningForegroundAsync(request);

        if (!listening)
        {
            // Fallback: use polling loop if listener API is not supported
            LastStatus = "Listener not supported, falling back to polling...";
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            Geolocation.Default.ListeningFailed -= OnListeningFailed;
            IsTracking = true;
            _cts = new CancellationTokenSource();
            _ = PollLoopAsync(_cts.Token);
            return true;
        }

        IsTracking = true;
        _updateCount = 0;
        LastStatus = "GPS active, waiting for fix...";
        return true;
    }

    private CancellationTokenSource? _cts;
    private int _updateCount;

    public Task StopAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Geolocation.Default.StopListeningForeground();
        Geolocation.Default.LocationChanged -= OnLocationChanged;
        Geolocation.Default.ListeningFailed -= OnListeningFailed;

        IsTracking = false;
        StopPlatformForegroundService();

        LastStatus = "Stopped";
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        _updateCount++;
        var location = e.Location;
        LastStatus = $"GPS #{_updateCount}: {location.Latitude:F5},{location.Longitude:F5} acc:{location.Accuracy:F0}m";

        EmitPoint(location);
    }

    private void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        LastStatus = $"Listener failed: {e.Error}. Falling back to polling...";

        Geolocation.Default.LocationChanged -= OnLocationChanged;
        Geolocation.Default.ListeningFailed -= OnListeningFailed;

        // Fallback to polling
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    private void EmitPoint(Location location)
    {
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

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _updateCount++;
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
                var location = await Geolocation.Default.GetLocationAsync(request, ct);

                if (location is not null)
                {
                    LastStatus = $"Poll #{_updateCount}: {location.Latitude:F5},{location.Longitude:F5} acc:{location.Accuracy:F0}m";
                    EmitPoint(location);
                }
                else
                {
                    LastStatus = $"Poll #{_updateCount}: no location";
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastStatus = $"Poll #{_updateCount}: {ex.GetType().Name}: {ex.Message}";
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { break; }
        }
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
