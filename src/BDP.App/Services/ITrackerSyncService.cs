using BDP.App.Models;

namespace BDP.App.Services;

public interface ITrackerSyncService
{
    bool IsScanning { get; }
    bool IsConnected { get; }
    bool IsPaired { get; }
    bool NeedsPairing { get; }
    string Status { get; }

    event Action? StatusChanged;

    Task<bool> ScanAndConnectAsync(CancellationToken ct = default);
    Task<bool> PairAsync();
    Task<int> GetPointCountAsync();
    Task<List<TrackPoint>> DownloadPointsAsync(IProgress<int>? progress = null);
    Task SeekAsync(int index);
    Task ClearDeviceAsync();
    Task<string> GetTrackerStateAsync();
    Task<TrackerBattery?> GetBatteryAsync();
    Task DisconnectAsync();
}
