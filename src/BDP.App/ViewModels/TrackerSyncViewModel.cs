using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class TrackerSyncViewModel : ObservableObject, IDisposable
{
    private readonly ITrackerSyncService _sync;
    private readonly IDatabaseService _db;
    private readonly UploadService _upload;
    private Timer? _scanTimer;
    private bool _syncing;
    private bool _pageActive;
    private int _scanCount;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _hasTracker;

    [ObservableProperty]
    private string _deviceName = "";

    [ObservableProperty]
    private string _lastSynced = "Never";

    [ObservableProperty]
    private string _trackerStatus = "";

    [ObservableProperty]
    private string _pairButtonText = "Pair New Device";

    [ObservableProperty]
    private bool _isPairing;

    public TrackerSyncViewModel(ITrackerSyncService sync, IDatabaseService db, UploadService upload)
    {
        _sync = sync;
        _db = db;
        _upload = upload;
        _sync.StatusChanged += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            Status = _sync.Status;
        });
    }

    public async Task OnPageOpenAsync()
    {
        _pageActive = true;
        await LoadTrackerAsync();

        if (!HasTracker)
        {
            // No device paired — start scanning for new device immediately
            await PairNewAsync();
        }
        else
        {
            // Start background scan loop to sync paired devices
            StartScanLoop();
        }
    }

    public void OnPageClose()
    {
        _pageActive = false;
        _scanTimer?.Dispose();
        _scanTimer = null;
        // Disconnect if still connected
        _ = _sync.DisconnectAsync();
    }

    private async Task LoadTrackerAsync()
    {
        var tracker = await _db.GetTrackerAsync();
        HasTracker = tracker is not null;

        if (tracker is not null)
        {
            DeviceName = tracker.DeviceName;
            LastSynced = tracker.LastSyncAt.HasValue
                ? FormatTimeAgo(tracker.LastSyncAt.Value)
                : "Never";
        }
    }

    private void StartScanLoop()
    {
        _scanTimer?.Dispose();
        // Scan immediately, then every 15 seconds while page is open
        _scanTimer = new Timer(_ => _ = ScanAndSyncAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    private async Task ScanAndSyncAsync()
    {
        if (!_pageActive || _syncing) return;
        _syncing = true;

        try
        {
            if (!_sync.IsConnected)
            {
                _scanCount++;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TrackerStatus = "Not in range";
                    Status = $"Scanning... ({_scanCount})";
                });
                var connected = await _sync.ScanAndConnectAsync();
                if (!connected)
                    return;
            }

            var trackerState = await _sync.GetTrackerStateAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TrackerStatus = trackerState switch
                {
                    "ACQUIRING" => "Acquiring GPS...",
                    "TRACKING" => "Recording ride...",
                    "PAUSED" => "Paused",
                    _ => "Connected"
                };
            });

            var tracker = await _db.GetTrackerAsync();
            if (tracker is not null)
            {
                tracker.LastSeenAt = DateTimeOffset.UtcNow;
                await _db.SaveTrackerAsync(tracker);
                MainThread.BeginInvokeOnMainThread(() => LastSynced = "Just now");
            }

            var count = await _sync.GetPointCountAsync();

            // Download new points if available (regardless of state — for speed)
            if (count > 0 && tracker is not null && count > tracker.LastSyncedIndex)
            {
                if (tracker.LastSyncedIndex > 0)
                {
                    await _sync.SeekAsync(tracker.LastSyncedIndex);
                    SetStatus($"Downloading from point {tracker.LastSyncedIndex}...");
                }
                else
                {
                    SetStatus($"Downloading {count} points...");
                }

                var newPoints = await _sync.DownloadPointsAsync();
                if (newPoints.Count > 0)
                {
                    var buffered = JsonSerializer.Deserialize<List<TrackPoint>>(tracker.BufferedPointsJson) ?? [];
                    buffered.AddRange(newPoints);
                    tracker.BufferedPointsJson = JsonSerializer.Serialize(buffered);
                    tracker.LastSyncedIndex = count;
                    await _db.SaveTrackerAsync(tracker);
                    SetStatus($"Buffered {buffered.Count} points");
                }
            }

            // Finalize completed rides from buffer
            if (tracker is not null)
            {
                var buffered = JsonSerializer.Deserialize<List<TrackPoint>>(tracker.BufferedPointsJson) ?? [];
                if (buffered.Count >= 2)
                {
                    var trips = TrackerSyncService.SplitIntoTrips(buffered);
                    bool isActive = trackerState is "TRACKING" or "PAUSED" or "ACQUIRING";

                    // If device is active, keep the last trip (still recording)
                    // If IDLE, finalize all trips
                    var tripsToFinalize = isActive && trips.Count > 1
                        ? trips.Take(trips.Count - 1).ToList()
                        : isActive && trips.Count == 1
                            ? new List<List<TrackPoint>>()  // only 1 trip and still active — wait
                            : trips;  // IDLE — finalize all

                    if (tripsToFinalize.Count > 0)
                    {
                        var ridesAdded = 0;
                        var totalDist = 0.0;
                        var pointsFinalized = 0;

                        foreach (var trip in tripsToFinalize)
                        {
                            var dist = 0.0;
                            for (var i = 1; i < trip.Count; i++)
                                dist += RideTracker.HaversineDistance(
                                    trip[i - 1].Latitude, trip[i - 1].Longitude,
                                    trip[i].Latitude, trip[i].Longitude);

                            var ride = new RideRecord
                            {
                                StartTime = trip[0].Timestamp,
                                EndTime = trip[^1].Timestamp,
                                DistanceMeters = dist,
                                Duration = trip[^1].Timestamp - trip[0].Timestamp,
                                TrackPointsJson = JsonSerializer.Serialize(trip),
                                TrackerId = tracker.Id,
                                IsUploaded = false
                            };

                            await _db.SaveRideAsync(ride);
                            ridesAdded++;
                            totalDist += dist;
                            pointsFinalized += trip.Count;
                        }

                        // Keep only the unfinalized points in the buffer
                        var remaining = buffered.Skip(pointsFinalized).ToList();
                        tracker.BufferedPointsJson = JsonSerializer.Serialize(remaining);
                        tracker.LastSyncAt = DateTimeOffset.UtcNow;
                        tracker.TotalPointsSynced += pointsFinalized;
                        tracker.TotalRidesSynced += ridesAdded;

                        // Clear device only when IDLE and everything is finalized
                        if (!isActive && remaining.Count == 0)
                        {
                            await _sync.ClearDeviceAsync();
                            tracker.LastSyncedIndex = 0;
                        }

                        await _db.SaveTrackerAsync(tracker);
                        MainThread.BeginInvokeOnMainThread(() => LastSynced = "Just now");
                        SetStatus($"Synced {ridesAdded} ride(s), {totalDist / 1000.0:F1} km");
                        await LoadTrackerAsync();
                        _ = _upload.UploadPendingAsync();
                    }
                    else if (isActive)
                    {
                        SetStatus($"Ride in progress — {buffered.Count} points buffered");
                    }
                    else
                    {
                        SetStatus("Up to date");
                    }
                }
                else
                {
                    SetStatus(trackerState == "IDLE" ? "Up to date" : "Ride starting...");
                }
            }
            // Disconnect after each check so device can sleep
            await _sync.DisconnectAsync();
        }
        catch
        {
            SetStatus("Sync failed — will retry");
            try { await _sync.DisconnectAsync(); } catch { }
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand]
    private async Task PairNewAsync()
    {
        if (IsPairing) return;
        IsPairing = true;

        var scanStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
        if (scanStatus != PermissionStatus.Granted)
        {
            scanStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
            if (scanStatus != PermissionStatus.Granted)
            {
                SetStatus("Bluetooth permission denied");
                IsPairing = false;
                return;
            }
        }

        var locStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (locStatus != PermissionStatus.Granted)
        {
            locStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (locStatus != PermissionStatus.Granted)
            {
                SetStatus("Location permission needed for BLE");
                IsPairing = false;
                return;
            }
        }

        PairButtonText = "Scanning...";
        SetStatus("Looking for tracker...");

        // Keep scanning until found or page closes
        bool connected = false;
        int attempt = 0;
        while (_pageActive && !connected)
        {
            attempt++;
            SetStatus($"Scanning... (attempt {attempt})");
            connected = await _sync.ScanAndConnectAsync();
            if (!connected && _pageActive)
            {
                SetStatus($"Not found yet — retrying...");
                await Task.Delay(3000);
            }
        }

        if (!connected)
        {
            PairButtonText = "Pair New Device";
            IsPairing = false;
            return;
        }

        if (_sync.NeedsPairing)
        {
            PairButtonText = "Pairing...";
            var paired = await _sync.PairAsync();
            if (!paired)
            {
                PairButtonText = "Pair New Device";
                SetStatus("Pairing failed");
                await _sync.DisconnectAsync();
                IsPairing = false;
                return;
            }
        }
        else if (!_sync.IsPaired)
        {
            PairButtonText = "Pair New Device";
            SetStatus("Device already paired to another phone. Factory reset it first.");
            await _sync.DisconnectAsync();
            IsPairing = false;
            return;
        }

        var tracker = new PairedTracker
        {
            DeviceName = "BikeTracker",
            PairedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        await _db.SaveTrackerAsync(tracker);

        await LoadTrackerAsync();
        MainThread.BeginInvokeOnMainThread(() => TrackerStatus = "Connected");
        PairButtonText = "Pair New Device";
        SetStatus("Paired successfully");
        IsPairing = false;

        // Start syncing — device is already connected
        StartScanLoop();
    }

    [RelayCommand]
    private async Task RemoveTrackerAsync()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
        await _sync.DisconnectAsync();
        await _db.DeleteTrackerAsync();
        HasTracker = false;
        SetStatus("Device removed");

        // Start scanning for new device immediately
        await PairNewAsync();
    }

    private void SetStatus(string status)
    {
        MainThread.BeginInvokeOnMainThread(() => Status = status);
    }

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var ago = DateTimeOffset.UtcNow - time;
        if (ago.TotalMinutes < 1) return "Just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    public void Dispose()
    {
        _scanTimer?.Dispose();
    }
}
