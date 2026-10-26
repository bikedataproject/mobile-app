using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

/// <summary>
/// UI for the paired tracker. Syncing itself lives in TrackerAutoSyncService — this used to
/// carry a second copy of that logic on its own timer, and the two disagreed about how to
/// finalize rides.
/// </summary>
public partial class TrackerSyncViewModel : ObservableObject, IDisposable
{
    private readonly ITrackerSyncService _sync;
    private readonly IDatabaseService _db;
    private readonly TrackerAutoSyncService _autoSync;
    private bool _pageActive;
    private bool _subscribed;

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

    [ObservableProperty]
    private string _batteryStatus = "";

    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private bool _batteryIsLow;

    [ObservableProperty]
    private string _batteryWarning = "";

    public TrackerSyncViewModel(ITrackerSyncService sync, IDatabaseService db,
        TrackerAutoSyncService autoSync)
    {
        _sync = sync;
        _db = db;
        _autoSync = autoSync;

        _sync.StatusChanged += () => SetStatus(_sync.Status);
    }

    // Tied to the page lifecycle, not the constructor: the view model is transient while
    // TrackerAutoSyncService is a singleton, so constructor-time subscriptions would
    // accumulate one dead handler per navigation.
    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        _autoSync.SyncStatusChanged += SetStatus;
        _autoSync.TrackerStateChanged += OnTrackerStateChanged;
        _autoSync.BatteryChanged += OnBatteryChanged;
        _autoSync.AutoSyncCompleted += OnSyncCompleted;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _autoSync.SyncStatusChanged -= SetStatus;
        _autoSync.TrackerStateChanged -= OnTrackerStateChanged;
        _autoSync.BatteryChanged -= OnBatteryChanged;
        _autoSync.AutoSyncCompleted -= OnSyncCompleted;
    }

    private void OnSyncCompleted(string _) =>
        MainThread.BeginInvokeOnMainThread(() => LastSynced = "Just now");

    public async Task OnPageOpenAsync()
    {
        _pageActive = true;
        Subscribe();
        await LoadTrackerAsync();

        if (!HasTracker)
        {
            await PairNewAsync();
            return;
        }

        // Poll faster while the user is looking at the page.
        _autoSync.SetForeground(true);
        _ = _autoSync.SyncNowAsync();
    }

    public void OnPageClose()
    {
        _pageActive = false;
        _autoSync.SetForeground(false);
        Unsubscribe();
    }

    private void OnTrackerStateChanged(string state) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TrackerStatus = state switch
            {
                "ACQUIRING" => "Acquiring GPS...",
                "TRACKING" => "Recording ride...",
                "PAUSED" => "Paused",
                "IDLE" => "Connected",
                _ => "Connected"
            };
        });

    private void OnBatteryChanged(TrackerBattery battery) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            HasBattery = true;
            BatteryStatus = $"{battery.Percent}% ({battery.Millivolts / 1000.0:F2} V)";
            BatteryIsLow = battery.IsLow || battery.IsCritical;

            // Critical means the device has already stopped starting new rides, so the
            // actionable advice differs: sync now vs replace soon.
            BatteryWarning = battery.IsCritical
                ? "Battery critical — the tracker has stopped recording. Sync now, then replace the batteries."
                : battery.IsLow
                    ? "Battery low — replace the batteries soon."
                    : "";
        });

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

    [RelayCommand]
    private async Task SyncNowAsync() => await _autoSync.SyncNowAsync();

    [RelayCommand]
    private async Task PairNewAsync()
    {
        if (IsPairing) return;
        IsPairing = true;

        // Hold off the sync timer — it would scan and connect underneath the pairing flow.
        _autoSync.Suspended = true;

        try
        {
            var scanStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (scanStatus != PermissionStatus.Granted)
            {
                scanStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
                if (scanStatus != PermissionStatus.Granted)
                {
                    SetStatus("Bluetooth permission denied");
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
                    return;
                }
            }

            PairButtonText = "Scanning...";
            SetStatus("Looking for tracker...");

            // Keep scanning until found or the page closes.
            var connected = false;
            var attempt = 0;
            while (_pageActive && !connected)
            {
                attempt++;
                SetStatus($"Scanning... (attempt {attempt})");
                connected = await _sync.ScanAndConnectAsync();
                if (!connected && _pageActive)
                {
                    SetStatus("Not found yet — retrying...");
                    await Task.Delay(3000);
                }
            }

            if (!connected)
            {
                PairButtonText = "Pair New Device";
                return;
            }

            if (_sync.NeedsPairing)
            {
                PairButtonText = "Pairing...";
                if (!await _sync.PairAsync())
                {
                    PairButtonText = "Pair New Device";
                    SetStatus("Pairing failed");
                    await _sync.DisconnectAsync();
                    return;
                }
            }
            else if (!_sync.IsPaired)
            {
                PairButtonText = "Pair New Device";
                SetStatus("Device already paired to another phone. Factory reset it first.");
                await _sync.DisconnectAsync();
                return;
            }

            await _db.SaveTrackerAsync(new PairedTracker
            {
                DeviceName = "BikeTracker",
                PairedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            });

            await LoadTrackerAsync();
            MainThread.BeginInvokeOnMainThread(() => TrackerStatus = "Connected");
            PairButtonText = "Pair New Device";
            SetStatus("Paired successfully");

            // Hand the live connection over to the sync routine.
            await _sync.DisconnectAsync();
            _autoSync.SetForeground(true);
        }
        finally
        {
            _autoSync.Suspended = false;
            IsPairing = false;
        }

        _ = _autoSync.SyncNowAsync();
    }

    [RelayCommand]
    private async Task RemoveTrackerAsync()
    {
        _autoSync.SetForeground(false);
        await _sync.DisconnectAsync();
        await _db.DeleteTrackerAsync();
        HasTracker = false;
        HasBattery = false;
        BatteryIsLow = false;
        BatteryWarning = "";
        SetStatus("Device removed");

        await PairNewAsync();
    }

    private void SetStatus(string status) =>
        MainThread.BeginInvokeOnMainThread(() => Status = status);

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var ago = DateTimeOffset.UtcNow - time;
        if (ago.TotalMinutes < 1) return "Just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    public void Dispose() => Unsubscribe();
}
