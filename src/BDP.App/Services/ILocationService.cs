using BDP.App.Models;

namespace BDP.App.Services;

public interface ILocationService
{
    bool IsTracking { get; }
    event Action<TrackPoint>? LocationUpdated;

    Task<bool> StartAsync();
    Task StopAsync();
}
