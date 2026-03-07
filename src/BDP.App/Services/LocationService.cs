using BDP.App.Models;

namespace BDP.App.Services;

public sealed class LocationService : ILocationService
{
    private CancellationTokenSource? _cts;

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

        LastStatus = "Permission granted. Starting location tracking...";

        StartPlatformForegroundService();

        IsTracking = true;
        _cts = new CancellationTokenSource();
        _ = TrackLoopAsync(_cts.Token);
        return true;
    }

    public Task StopAsync()
    {
        IsTracking = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        StopPlatformForegroundService();

        LastStatus = "Stopped";
        return Task.CompletedTask;
    }

    private async Task TrackLoopAsync(CancellationToken ct)
    {
        var readCount = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                readCount++;
                LastStatus = $"Reading #{readCount}...";
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));
                var location = await Geolocation.Default.GetLocationAsync(request, ct);

                if (location is not null)
                {
                    LastStatus = $"Got #{readCount}: {location.Latitude:F5},{location.Longitude:F5} acc:{location.Accuracy:F0}m";
                    var point = new TrackPoint
                    {
                        Longitude = location.Longitude,
                        Latitude = location.Latitude,
                        Elevation = location.Altitude,
                        Timestamp = DateTimeOffset.UtcNow,
                        Accuracy = location.Accuracy ?? double.MaxValue,
                        Speed = location.Speed
                    };

                    LocationUpdated?.Invoke(point);
                }
                else
                {
                    LastStatus = $"Read #{readCount}: null location returned";
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (FeatureNotSupportedException)
            {
                LastStatus = "GPS not supported on this device";
                break;
            }
            catch (FeatureNotEnabledException)
            {
                LastStatus = "GPS is disabled. Enable Location in device settings.";
                break;
            }
            catch (PermissionException ex)
            {
                LastStatus = $"Permission error: {ex.Message}";
                break;
            }
            catch (Exception ex)
            {
                LastStatus = $"Error #{readCount}: {ex.GetType().Name}: {ex.Message}";
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
