using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class RecordViewModel : ObservableObject, IDisposable
{
    private readonly IRideTracker _tracker;
    private readonly IDatabaseService _db;
    private readonly UploadService _upload;
    private readonly AutoRideDetectionService _autoDetection;
    private IDispatcherTimer? _timer;
    private bool _isAutoRecording;

    [ObservableProperty]
    private string _statusMessage = "Tap to record your ride";

    [ObservableProperty]
    private string _statusDetail = "";

    [ObservableProperty]
    private bool _isStatusDetailVisible;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _syncMessage = "";

    [ObservableProperty]
    private bool _isSyncVisible;

    [ObservableProperty]
    private string _syncIcon = "";

    [ObservableProperty]
    private bool _isAutoDetectionStatusVisible;

    [ObservableProperty]
    private string _autoDetectionStatusText = "";

    [ObservableProperty]
    private Microsoft.Maui.Graphics.Color _autoDetectionDotColor = Colors.Gray;

    public RecordViewModel(IRideTracker tracker, IDatabaseService db, UploadService upload, AutoRideDetectionService autoDetection)
    {
        _tracker = tracker;
        _db = db;
        _upload = upload;
        _autoDetection = autoDetection;
        _upload.SyncStateChanged += OnSyncStateChanged;
        _autoDetection.StateChanged += OnAutoDetectionStateChanged;
        _autoDetection.StatusMessage += OnAutoDetectionStatusMessage;

        UpdateAutoDetectionDisplay();
    }

    public void StartTimer(IDispatcher dispatcher)
    {
        if (_timer is not null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateDisplay();
        _timer.Start();
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        // If auto-recording is active, user tapping stop ends the auto-ride
        if (_isAutoRecording)
        {
            await StopAutoRecordingAsync();
            return;
        }

        if (_tracker.State == RideState.Idle)
        {
            await StartRecordingAsync();
        }
        else if (_tracker.State == RideState.Recording)
        {
            await StopRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        // Pause auto-detection during manual recording
        _autoDetection.OnManualRecordingStarted();

        IsSyncVisible = false;
        IsStatusDetailVisible = false;
        StatusMessage = "Starting GPS...";
        await _tracker.StartAsync();

        if (_tracker.State == RideState.Recording)
        {
            IsRecording = true;
            StatusMessage = "Recording...";
        }
        else
        {
            StatusMessage = "Could not start GPS.\nTap to try again.";
            _autoDetection.OnManualRecordingStopped();
        }
    }

    private async Task StopRecordingAsync()
    {
        var ride = await _tracker.StopAsync();
        IsRecording = false;
        IsStatusDetailVisible = false;

        // Resume auto-detection
        _autoDetection.OnManualRecordingStopped();

        if (ride.TrackPointsJson == "[]" || _tracker.Points.Count < 2)
        {
            StatusMessage = "Not enough GPS data recorded.\nTap to try again.";
            return;
        }

        ride.Source = "manual";
        var distanceKm = ride.DistanceMeters / 1000.0;
        var duration = ride.Duration.ToString(@"hh\:mm\:ss");
        StatusMessage = $"Ride saved! {distanceKm:F1} km in {duration}";
        IsStatusDetailVisible = false;

        await _db.SaveRideAsync(ride);

        // Trigger upload
        _ = _upload.UploadPendingAsync();
    }

    private async Task StopAutoRecordingAsync()
    {
        // Force-finalize the auto-recorded ride
        await _autoDetection.DisableAsync();
        _isAutoRecording = false;
        IsRecording = false;
        IsStatusDetailVisible = false;
        StatusMessage = "Auto-recorded ride saved";

        // Re-enable if it was previously on
        var prefs = new PreferencesService();
        if (prefs.AutoDetectionEnabled)
        {
            _ = _autoDetection.EnableAsync();
        }
    }

    private void OnSyncStateChanged(SyncState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Hide sync status during recording
            if (IsRecording)
            {
                IsSyncVisible = false;
                return;
            }

            switch (state)
            {
                case SyncState.Hidden:
                    IsSyncVisible = false;
                    break;
                case SyncState.Uploading:
                    IsSyncVisible = true;
                    SyncIcon = "\u2B6E"; // ⭮ cycle arrow
                    SyncMessage = "Uploading your ride...";
                    break;
                case SyncState.Synced:
                    IsSyncVisible = true;
                    SyncIcon = "\u2714"; // ✔
                    SyncMessage = "All rides synced — thanks for contributing!";
                    StatusMessage = "Tap to record your next ride";
                    break;
                case SyncState.Failed:
                    IsSyncVisible = true;
                    SyncIcon = "\u26A0"; // ⚠
                    SyncMessage = "Upload failed, will retry automatically";
                    StatusMessage = "Tap to record your next ride";
                    break;
            }
        });
    }

    private void UpdateDisplay()
    {
        // Handle auto-recording display
        if (_isAutoRecording && _autoDetection.State is AutoDetectionState.Recording or AutoDetectionState.Finalizing)
        {
            var distanceM = _autoDetection.DistanceMeters;
            var distance = distanceM >= 1000
                ? $"{distanceM / 1000.0:F2} km"
                : $"{distanceM:F0} m";

            var duration = _autoDetection.Duration.ToString(@"hh\:mm\:ss");
            StatusMessage = $"{distance} | {duration} (Auto)";

            var points = _autoDetection.Points;
            var lastPoint = points.Count > 0 ? points[^1] : null;
            StatusDetail = lastPoint is not null ? $"GPS accuracy: \u00B1{lastPoint.Accuracy:F0}m" : "Waiting for GPS...";
            IsStatusDetailVisible = true;
            return;
        }

        if (_tracker.State != RideState.Recording) return;

        var distM = _tracker.DistanceMeters;
        var dist = distM >= 1000
            ? $"{distM / 1000.0:F2} km"
            : $"{distM:F0} m";

        var dur = _tracker.Duration.ToString(@"hh\:mm\:ss");
        StatusMessage = $"{dist} | {dur}";

        var lp = _tracker.Points.Count > 0 ? _tracker.Points[^1] : null;
        StatusDetail = lp is not null ? $"GPS accuracy: \u00B1{lp.Accuracy:F0}m" : "Waiting for GPS...";
        IsStatusDetailVisible = true;
    }

    private void OnAutoDetectionStateChanged(AutoDetectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateAutoDetectionDisplay();

            if (state == AutoDetectionState.Recording)
            {
                _isAutoRecording = true;
                IsRecording = true;
                IsSyncVisible = false;
                StatusMessage = "Auto-recording ride...";
            }
            else if (state == AutoDetectionState.Finalizing)
            {
                StatusMessage = "Ride may have ended, waiting...";
            }
            else if (state == AutoDetectionState.Idle && _isAutoRecording)
            {
                _isAutoRecording = false;
                IsRecording = false;
                IsStatusDetailVisible = false;
            }
        });
    }

    private void OnAutoDetectionStatusMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_autoDetection.State != AutoDetectionState.Disabled && !IsRecording)
            {
                StatusMessage = message;
            }
        });
    }

    private void UpdateAutoDetectionDisplay()
    {
        var state = _autoDetection.State;
        IsAutoDetectionStatusVisible = state != AutoDetectionState.Disabled;

        switch (state)
        {
            case AutoDetectionState.Idle:
                AutoDetectionStatusText = "Auto-detection active";
                AutoDetectionDotColor = Colors.LimeGreen;
                break;
            case AutoDetectionState.Confirming:
                AutoDetectionStatusText = "Cycling detected, confirming...";
                AutoDetectionDotColor = Colors.Orange;
                break;
            case AutoDetectionState.Recording:
                AutoDetectionStatusText = "Auto-recording (Auto)";
                AutoDetectionDotColor = Colors.Red;
                break;
            case AutoDetectionState.Finalizing:
                AutoDetectionStatusText = "Finalizing ride...";
                AutoDetectionDotColor = Colors.Orange;
                break;
            default:
                AutoDetectionStatusText = "";
                break;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _upload.SyncStateChanged -= OnSyncStateChanged;
        _autoDetection.StateChanged -= OnAutoDetectionStateChanged;
        _autoDetection.StatusMessage -= OnAutoDetectionStatusMessage;
    }
}
