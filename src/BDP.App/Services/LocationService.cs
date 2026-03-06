using BDP.App.Models;

namespace BDP.App.Services;

public sealed class LocationService : ILocationService
{
    private CancellationTokenSource? _cts;

    public bool IsTracking { get; private set; }
    public event Action<TrackPoint>? LocationUpdated;

    public async Task<bool> StartAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted) return false;
        }

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
        return Task.CompletedTask;
    }

    private async Task TrackLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
                var location = await Geolocation.Default.GetLocationAsync(request, ct);
                if (location is not null)
                {
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
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // GPS read failed, retry on next interval
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
