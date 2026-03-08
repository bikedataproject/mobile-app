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
    private IDispatcherTimer? _timer;

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

    public RecordViewModel(IRideTracker tracker, IDatabaseService db, UploadService upload)
    {
        _tracker = tracker;
        _db = db;
        _upload = upload;
        _upload.SyncStateChanged += OnSyncStateChanged;
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
        }
    }

    private async Task StopRecordingAsync()
    {
        var ride = await _tracker.StopAsync();
        IsRecording = false;
        IsStatusDetailVisible = false;

        if (ride.TrackPointsJson == "[]" || _tracker.Points.Count < 2)
        {
            StatusMessage = "Not enough GPS data recorded.\nTap to try again.";
            return;
        }


        var distanceKm = ride.DistanceMeters / 1000.0;
        var duration = ride.Duration.ToString(@"hh\:mm\:ss");
        StatusMessage = $"Ride saved! {distanceKm:F1} km in {duration}";
        IsStatusDetailVisible = false;

        await _db.SaveRideAsync(ride);

        // Trigger upload
        _ = _upload.UploadPendingAsync();
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
        if (_tracker.State != RideState.Recording) return;

        var distanceM = _tracker.DistanceMeters;
        var distance = distanceM >= 1000
            ? $"{distanceM / 1000.0:F2} km"
            : $"{distanceM:F0} m";

        var duration = _tracker.Duration.ToString(@"hh\:mm\:ss");
        StatusMessage = $"{distance} | {duration}";

        var lastPoint = _tracker.Points.Count > 0 ? _tracker.Points[^1] : null;
        StatusDetail = lastPoint is not null ? $"GPS accuracy: \u00B1{lastPoint.Accuracy:F0}m" : "Waiting for GPS...";
        IsStatusDetailVisible = true;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _upload.SyncStateChanged -= OnSyncStateChanged;
    }
}
