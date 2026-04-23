using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

public sealed class TrackerAutoSyncService : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    private readonly ITrackerSyncService _sync;
    private readonly IDatabaseService _db;
    private readonly UploadService _upload;
    private Timer? _timer;
    private bool _syncing;

    public event Action<string>? AutoSyncCompleted;
    public event Action<string>? TrackerStateChanged;
    public event Action<string>? SyncStatusChanged;

    public TrackerAutoSyncService(ITrackerSyncService sync, IDatabaseService db, UploadService upload)
    {
        _sync = sync;
        _db = db;
        _upload = upload;
    }

    public void Start()
    {
        _timer = new Timer(_ => _ = TrySyncAsync(), null, ScanInterval, ScanInterval);
    }

    private async Task TrySyncAsync()
    {
        if (_syncing || _sync.IsConnected) return;

        _syncing = true;
        try
        {
            SyncStatusChanged?.Invoke("Scanning...");
            var connected = await _sync.ScanAndConnectAsync();
            if (!connected)
            {
                SyncStatusChanged?.Invoke("Auto-sync active");
                return;
            }

            if (!_sync.IsPaired || _sync.NeedsPairing)
            {
                SyncStatusChanged?.Invoke("Auto-sync active");
                await _sync.DisconnectAsync();
                return;
            }

            SyncStatusChanged?.Invoke("Connected — reading state...");
            var trackerState = await _sync.GetTrackerStateAsync();
            TrackerStateChanged?.Invoke(trackerState);

            var tracker = await _db.GetTrackerAsync();
            if (tracker is not null)
            {
                tracker.LastSeenAt = DateTimeOffset.UtcNow;
                await _db.SaveTrackerAsync(tracker);
            }

            var count = await _sync.GetPointCountAsync();
            if (count < 2)
            {
                SyncStatusChanged?.Invoke(count == 0 ? "No points on device" : "Auto-sync active");
                await _sync.DisconnectAsync();
                return;
            }

            // Resume from last synced index
            var lastIndex = tracker?.LastSyncedIndex ?? 0;
            if (lastIndex > 0 && lastIndex < count)
            {
                SyncStatusChanged?.Invoke($"Resuming from point {lastIndex}...");
                await _sync.SeekAsync(lastIndex);
            }

            SyncStatusChanged?.Invoke($"Downloading {count} points...");
            var points = await _sync.DownloadPointsAsync();
            if (points.Count < 2)
            {
                SyncStatusChanged?.Invoke("Auto-sync active");
                await _sync.DisconnectAsync();
                return;
            }

            SyncStatusChanged?.Invoke($"Processing {points.Count} points...");
            // Split into trips
            var trips = TrackerSyncService.SplitIntoTrips(points);
            var ridesAdded = 0;

            foreach (var trip in trips)
            {
                var totalDistance = 0.0;
                for (var i = 1; i < trip.Count; i++)
                {
                    totalDistance += RideTracker.HaversineDistance(
                        trip[i - 1].Latitude, trip[i - 1].Longitude,
                        trip[i].Latitude, trip[i].Longitude);
                }

                var ride = new RideRecord
                {
                    StartTime = trip[0].Timestamp,
                    EndTime = trip[^1].Timestamp,
                    DistanceMeters = totalDistance,
                    TrackerId = tracker?.Id,
                    Duration = trip[^1].Timestamp - trip[0].Timestamp,
                    TrackPointsJson = JsonSerializer.Serialize(trip),
                    IsUploaded = false
                };

                await _db.SaveRideAsync(ride);
                ridesAdded++;
            }

            // Only clear if device is idle (not mid-ride)
            if (trackerState != "TRACKING")
            {
                await _sync.ClearDeviceAsync();
            }

            await _sync.DisconnectAsync();

            if (tracker is not null)
            {
                tracker.LastSyncAt = DateTimeOffset.UtcNow;
                tracker.TotalPointsSynced += points.Count;
                tracker.TotalRidesSynced += ridesAdded;
                tracker.LastSyncedIndex = trackerState == "TRACKING" ? count : 0;
                await _db.SaveTrackerAsync(tracker);
            }

            var totalDist = trips.Sum(t =>
            {
                var d = 0.0;
                for (var i = 1; i < t.Count; i++)
                    d += RideTracker.HaversineDistance(
                        t[i - 1].Latitude, t[i - 1].Longitude,
                        t[i].Latitude, t[i].Longitude);
                return d;
            });

            AutoSyncCompleted?.Invoke($"Synced {ridesAdded} ride(s), {totalDist / 1000.0:F1} km");

            _ = _upload.UploadPendingAsync();
        }
        catch
        {
            try { await _sync.DisconnectAsync(); } catch { }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
