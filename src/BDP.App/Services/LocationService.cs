using BDP.App.Models;

namespace BDP.App.Services;

public sealed class LocationService : ILocationService
{
    private CancellationTokenSource? _cts;
    private int _readCount;

    public bool IsTracking { get; private set; }
    public string LastStatus { get; private set; } = "Not started";
    public event Action<TrackPoint>? LocationUpdated;

    public async Task<bool> StartAsync()
    {
        LastStatus = "Checking permissions...";

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

        var alwaysStatus = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
        if (alwaysStatus != PermissionStatus.Granted)
        {
            alwaysStatus = await Permissions.RequestAsync<Permissions.LocationAlways>();
            LastStatus = $"Background location: {alwaysStatus}";
        }

        StartPlatformForegroundService();

        // Activate GPS hardware by starting the listener briefly
        LastStatus = "Activating GPS...";
        try
        {
            var activated = await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));
            if (activated)
            {
                // GPS is now powered on — stop the listener, we'll poll instead
                Geolocation.Default.StopListeningForeground();
            }
        }
        catch
        {
            // Ignore — we'll still try polling
        }

        IsTracking = true;
        _readCount = 0;
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);

        LastStatus = "GPS active, waiting for fix...";
        return true;
    }

    public Task StopAsync()
    {
        IsTracking = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try { Geolocation.Default.StopListeningForeground(); } catch { }

        StopPlatformForegroundService();

        LastStatus = "Stopped";
        return Task.CompletedTask;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _readCount++;
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
                var location = await Geolocation.Default.GetLocationAsync(request, ct);

                if (location is not null)
                {
                    LastStatus = $"GPS #{_readCount}: {location.Latitude:F5},{location.Longitude:F5} acc:{location.Accuracy:F0}m";

                    LocationUpdated?.Invoke(new TrackPoint
                    {
                        Longitude = location.Longitude,
                        Latitude = location.Latitude,
                        Elevation = location.Altitude,
                        Timestamp = location.Timestamp != default ? location.Timestamp : DateTimeOffset.UtcNow,
                        Accuracy = location.Accuracy ?? double.MaxValue,
                        Speed = location.Speed
                    });
                }
                else
                {
                    LastStatus = $"GPS #{_readCount}: no location";
                }
            }
            catch (OperationCanceledException) { break; }
            catch (FeatureNotSupportedException)
            {
                LastStatus = "GPS not supported on this device";
                break;
            }
            catch (FeatureNotEnabledException)
            {
                LastStatus = "GPS is disabled. Enable Location in settings.";
                break;
            }
            catch (PermissionException ex)
            {
                LastStatus = $"Permission error: {ex.Message}";
                break;
            }
            catch (Exception ex)
            {
                LastStatus = $"GPS #{_readCount} error: {ex.GetType().Name}: {ex.Message}";
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
