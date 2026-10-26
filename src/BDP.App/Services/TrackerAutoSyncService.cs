using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

/// <summary>
/// Owns the one and only tracker sync routine.
///
/// There used to be a second, near-duplicate implementation in TrackerSyncViewModel with
/// its own 15s timer. Both ran against the same ITrackerSyncService singleton and the same
/// PairedTracker row but disagreed about how to finalize rides, which produced duplicate
/// and fragmented RideRecords. The view model now drives this class instead of reimplementing
/// it, and _gate guarantees only one sync is ever in flight.
/// </summary>
public sealed class TrackerAutoSyncService : IDisposable
{
    private static readonly TimeSpan BackgroundInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ForegroundInterval = TimeSpan.FromSeconds(15);

    private readonly ITrackerSyncService _sync;
    private readonly IDatabaseService _db;
    private readonly UploadService _upload;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private TimeSpan _interval = BackgroundInterval;

    public event Action<string>? AutoSyncCompleted;
    public event Action<string>? TrackerStateChanged;
    public event Action<string>? SyncStatusChanged;
    public event Action<TrackerBattery>? BatteryChanged;

    /// <summary>
    /// Set while the pairing flow holds the connection, so the timer cannot scan underneath it.
    /// </summary>
    public bool Suspended { get; set; }

    public TrackerAutoSyncService(ITrackerSyncService sync, IDatabaseService db, UploadService upload)
    {
        _sync = sync;
        _db = db;
        _upload = upload;
    }

    public void Start() => Reschedule(BackgroundInterval);

    /// <summary>
    /// Polls every 15s while the tracker page is open, every 2 minutes otherwise.
    /// </summary>
    public void SetForeground(bool foreground) =>
        Reschedule(foreground ? ForegroundInterval : BackgroundInterval);

    private void Reschedule(TimeSpan interval)
    {
        _interval = interval;
        _timer?.Dispose();
        _timer = new Timer(_ => _ = SyncNowAsync(), null, interval, interval);
    }

    /// <summary>
    /// Runs one sync pass. Returns immediately if one is already running or pairing is in
    /// progress, so callers can fire this freely.
    /// </summary>
    public async Task SyncNowAsync()
    {
        if (Suspended) return;
        if (!await _gate.WaitAsync(0)) return;

        try
        {
            await RunSyncAsync();
        }
        catch
        {
            SyncStatusChanged?.Invoke("Sync failed — will retry");
            try { await _sync.DisconnectAsync(); } catch { }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSyncAsync()
    {
        var tracker = await _db.GetTrackerAsync();
        if (tracker is null) return;

        if (!_sync.IsConnected)
        {
            SyncStatusChanged?.Invoke("Scanning...");
            if (!await _sync.ScanAndConnectAsync())
            {
                SyncStatusChanged?.Invoke("Not in range");
                return;
            }
        }

        try
        {
            if (!_sync.IsPaired || _sync.NeedsPairing)
            {
                SyncStatusChanged?.Invoke("Device not paired to this phone");
                return;
            }

            var trackerState = await _sync.GetTrackerStateAsync();
            TrackerStateChanged?.Invoke(trackerState);

            // Battery is ungated on the device, so this works even without the key.
            var battery = await _sync.GetBatteryAsync();
            if (battery is not null) BatteryChanged?.Invoke(battery);

            tracker.LastSeenAt = DateTimeOffset.UtcNow;
            await _db.SaveTrackerAsync(tracker);

            var count = await _sync.GetPointCountAsync();

            // The device resets its batch cursor to 0 on every BLE connect, so a download
            // with nothing new to fetch would re-read points already saved as rides. Skip it.
            if (count > tracker.LastSyncedIndex)
            {
                if (tracker.LastSyncedIndex > 0)
                {
                    SyncStatusChanged?.Invoke($"Downloading from point {tracker.LastSyncedIndex}...");
                    await _sync.SeekAsync(tracker.LastSyncedIndex);
                }
                else
                {
                    SyncStatusChanged?.Invoke($"Downloading {count} points...");
                }

                var newPoints = await _sync.DownloadPointsAsync();
                if (newPoints.Count > 0)
                {
                    var buffer = Deserialize(tracker.BufferedPointsJson);
                    buffer.AddRange(newPoints);
                    tracker.BufferedPointsJson = JsonSerializer.Serialize(buffer);
                    tracker.LastSyncedIndex = count;
                    await _db.SaveTrackerAsync(tracker);
                    SyncStatusChanged?.Invoke($"Buffered {buffer.Count} points");
                }
            }

            await FinalizeAsync(tracker, trackerState);
        }
        finally
        {
            // Always hang up: a held connection keeps the tracker awake and out of deep sleep.
            await _sync.DisconnectAsync();
        }
    }

    /// <summary>
    /// Turns buffered points into rides. While the device is still active the final trip is
    /// left in the buffer, because more points of that same ride are still coming.
    /// </summary>
    private async Task FinalizeAsync(PairedTracker tracker, string trackerState)
    {
        var buffer = Deserialize(tracker.BufferedPointsJson);
        if (buffer.Count < 2)
        {
            SyncStatusChanged?.Invoke(trackerState == "IDLE" ? "Up to date" : "Ride starting...");
            return;
        }

        var isActive = trackerState is "TRACKING" or "PAUSED" or "ACQUIRING";
        var trips = TrackerSyncService.SplitIntoTrips(buffer);

        var toFinalize = isActive
            ? trips.Take(Math.Max(0, trips.Count - 1)).ToList()
            : trips;

        if (toFinalize.Count == 0)
        {
            SyncStatusChanged?.Invoke(isActive
                ? $"Ride in progress — {buffer.Count} points buffered"
                : "Up to date");
            return;
        }

        var ridesAdded = 0;
        var totalDistance = 0.0;

        foreach (var trip in toFinalize)
        {
            var distance = 0.0;
            for (var i = 1; i < trip.Count; i++)
                distance += RideTracker.HaversineDistance(
                    trip[i - 1].Latitude, trip[i - 1].Longitude,
                    trip[i].Latitude, trip[i].Longitude);

            await _db.SaveRideAsync(new RideRecord
            {
                StartTime = trip[0].Timestamp,
                EndTime = trip[^1].Timestamp,
                DistanceMeters = distance,
                Duration = trip[^1].Timestamp - trip[0].Timestamp,
                TrackPointsJson = JsonSerializer.Serialize(trip),
                TrackerId = tracker.Id,
                IsUploaded = false
            });

            ridesAdded++;
            totalDistance += distance;
        }

        // Carry over the trips that were not finalized, rather than skipping a point count.
        // SplitIntoTrips drops runs shorter than two points, so a positional skip would
        // misalign and re-finalize points that had already become rides.
        var remaining = trips.Skip(toFinalize.Count).SelectMany(t => t).ToList();
        var pointsFinalized = toFinalize.Sum(t => t.Count);

        tracker.BufferedPointsJson = JsonSerializer.Serialize(remaining);
        tracker.LastSyncAt = DateTimeOffset.UtcNow;
        tracker.TotalPointsSynced += pointsFinalized;
        tracker.TotalRidesSynced += ridesAdded;

        // Only wipe the device once the ride is over and nothing is still buffered.
        if (!isActive && remaining.Count == 0)
        {
            await _sync.ClearDeviceAsync();
            tracker.LastSyncedIndex = 0;
        }

        await _db.SaveTrackerAsync(tracker);

        var summary = $"Synced {ridesAdded} ride(s), {totalDistance / 1000.0:F1} km";
        SyncStatusChanged?.Invoke(summary);
        AutoSyncCompleted?.Invoke(summary);

        _ = _upload.UploadPendingAsync();
    }

    private static List<TrackPoint> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TrackPoint>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _gate.Dispose();
    }
}
