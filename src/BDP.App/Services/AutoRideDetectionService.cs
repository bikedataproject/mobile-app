using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

public enum AutoDetectionState
{
    Disabled,
    Idle,
    Confirming,
    Recording,
    Finalizing
}

public sealed class AutoRideDetectionService : IDisposable
{
    private const double MaxAccuracyMeters = 30.0;
    private const double MaxSpeedKmh = 150.0;
    private const double MinDistanceBetweenPointsMeters = 2.0;
    private static readonly TimeSpan FinalizingGracePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromHours(4);

    private readonly IActivityRecognitionService _activityService;
    private readonly ILocationService _locationService;
    private readonly IDatabaseService _db;
    private readonly UploadService _upload;
    private readonly PreferencesService _prefs;
    private readonly RidePostProcessor _postProcessor;

    private readonly List<TrackPoint> _points = [];
    private readonly List<TrackPoint> _confirmBuffer = [];
    private CancellationTokenSource? _confirmCts;
    private CancellationTokenSource? _finalizeCts;
    private DateTimeOffset _recordingStartTime;
    private double _distanceMeters;
    private double _currentSpeedKmh;
    private bool _manualRecordingActive;

    public AutoDetectionState State { get; private set; } = AutoDetectionState.Disabled;
    public IReadOnlyList<TrackPoint> Points => _points;
    public double DistanceMeters => _distanceMeters;
    public double CurrentSpeedKmh => _currentSpeedKmh;
    public TimeSpan Duration => State == AutoDetectionState.Recording && _points.Count > 0
        ? DateTimeOffset.UtcNow - _recordingStartTime
        : TimeSpan.Zero;

    public event Action<AutoDetectionState>? StateChanged;
    public event Action<string>? StatusMessage;

    public AutoRideDetectionService(
        IActivityRecognitionService activityService,
        ILocationService locationService,
        IDatabaseService db,
        UploadService upload,
        PreferencesService prefs,
        RidePostProcessor postProcessor)
    {
        _activityService = activityService;
        _locationService = locationService;
        _db = db;
        _upload = upload;
        _prefs = prefs;
        _postProcessor = postProcessor;
    }

    public async Task<bool> EnableAsync()
    {
        if (State != AutoDetectionState.Disabled) return true;

        if (!_activityService.IsAvailable)
        {
            StatusMessage?.Invoke("Activity Recognition not available on this device");
            return false;
        }

        var started = await _activityService.StartMonitoringAsync();
        if (!started)
        {
            StatusMessage?.Invoke("Could not start activity monitoring");
            return false;
        }

        _activityService.TransitionDetected += OnActivityTransition;
        SetState(AutoDetectionState.Idle);
        StatusMessage?.Invoke("Auto-detection active");
        return true;
    }

    public async Task DisableAsync()
    {
        _activityService.TransitionDetected -= OnActivityTransition;

        // Cancel any in-progress confirmation or finalization
        _confirmCts?.Cancel();
        _finalizeCts?.Cancel();

        // If we were recording, try to save what we have
        if (State == AutoDetectionState.Recording || State == AutoDetectionState.Finalizing)
        {
            await SaveAndUploadRideAsync();
            await StopLocationTracking();
        }

        await _activityService.StopMonitoringAsync();
        SetState(AutoDetectionState.Disabled);
    }

    /// <summary>
    /// Call when the user starts manual recording — pauses auto-detection.
    /// </summary>
    public void OnManualRecordingStarted()
    {
        _manualRecordingActive = true;
        _confirmCts?.Cancel();
        _finalizeCts?.Cancel();

        if (State == AutoDetectionState.Confirming)
        {
            _confirmBuffer.Clear();
            SetState(AutoDetectionState.Idle);
        }
        else if (State == AutoDetectionState.Recording || State == AutoDetectionState.Finalizing)
        {
            // Discard auto-recording in favor of manual
            _locationService.LocationUpdated -= OnLocationUpdated;
            _points.Clear();
            _distanceMeters = 0;
            SetState(AutoDetectionState.Idle);
        }
    }

    /// <summary>
    /// Call when the user stops manual recording — resumes auto-detection.
    /// </summary>
    public void OnManualRecordingStopped()
    {
        _manualRecordingActive = false;
    }

    private void OnActivityTransition(ActivityTransitionData data)
    {
        if (_manualRecordingActive) return;

        System.Diagnostics.Debug.WriteLine(
            $"AutoDetection: {data.ActivityType} {data.Transition} (state={State})");

        switch (State)
        {
            case AutoDetectionState.Idle:
                if (data.ActivityType == DetectedActivityType.OnBicycle &&
                    data.Transition == TransitionType.Enter)
                {
                    _ = BeginConfirmationAsync();
                }
                break;

            case AutoDetectionState.Confirming:
                // If we get a definitive non-cycling activity during confirmation, cancel
                if (data.ActivityType is DetectedActivityType.InVehicle
                    or DetectedActivityType.Still
                    or DetectedActivityType.OnFoot
                    && data.Transition == TransitionType.Enter)
                {
                    CancelConfirmation();
                }
                break;

            case AutoDetectionState.Recording:
                if (data.ActivityType is DetectedActivityType.Still
                    or DetectedActivityType.OnFoot
                    && data.Transition == TransitionType.Enter)
                {
                    _ = BeginFinalizingAsync();
                }
                else if (data.ActivityType == DetectedActivityType.InVehicle
                         && data.Transition == TransitionType.Enter)
                {
                    _ = BeginFinalizingAsync();
                }
                break;

            case AutoDetectionState.Finalizing:
                if (data.ActivityType == DetectedActivityType.OnBicycle
                    && data.Transition == TransitionType.Enter)
                {
                    // Resume recording — cancel finalization
                    _finalizeCts?.Cancel();
                    SetState(AutoDetectionState.Recording);
                    StatusMessage?.Invoke("Cycling resumed, continuing ride...");
                }
                break;
        }
    }

    private async Task BeginConfirmationAsync()
    {
        SetState(AutoDetectionState.Confirming);
        StatusMessage?.Invoke("Cycling detected, confirming...");

        _confirmBuffer.Clear();
        _confirmCts?.Cancel();
        _confirmCts = new CancellationTokenSource();
        var ct = _confirmCts.Token;

        // Start GPS at medium interval for confirmation
        _locationService.LocationUpdated += OnConfirmLocationUpdated;
        var gpsStarted = await _locationService.StartAsync();
        if (!gpsStarted)
        {
            _locationService.LocationUpdated -= OnConfirmLocationUpdated;
            SetState(AutoDetectionState.Idle);
            StatusMessage?.Invoke("Could not start GPS for confirmation");
            return;
        }

        try
        {
            await Task.Delay(_prefs.ConfirmationWindow, ct);
        }
        catch (OperationCanceledException)
        {
            return; // Confirmation was cancelled
        }

        _locationService.LocationUpdated -= OnConfirmLocationUpdated;

        // Validate confirmation buffer
        if (ValidateConfirmation())
        {
            TransitionToRecording();
        }
        else
        {
            await StopLocationTracking();
            _confirmBuffer.Clear();
            SetState(AutoDetectionState.Idle);
            StatusMessage?.Invoke("Not cycling — returning to monitoring");
        }
    }

    private void CancelConfirmation()
    {
        _confirmCts?.Cancel();
        _locationService.LocationUpdated -= OnConfirmLocationUpdated;
        _confirmBuffer.Clear();
        _ = StopLocationTracking();
        SetState(AutoDetectionState.Idle);
        StatusMessage?.Invoke("Confirmation cancelled — not cycling");
    }

    private bool ValidateConfirmation()
    {
        // Need at least 3 points with good accuracy
        var goodPoints = _confirmBuffer.Where(p => p.Accuracy <= MaxAccuracyMeters).ToList();
        if (goodPoints.Count < 3) return false;

        // Check displacement from first point
        var first = goodPoints[0];
        var last = goodPoints[^1];
        var displacement = RidePostProcessor.HaversineDistance(
            first.Latitude, first.Longitude, last.Latitude, last.Longitude);
        if (displacement < 50) return false; // Less than 50m displacement

        // Check average speed
        var totalDist = 0.0;
        var highSpeedCount = 0;
        for (int i = 1; i < goodPoints.Count; i++)
        {
            var dist = RidePostProcessor.HaversineDistance(
                goodPoints[i - 1].Latitude, goodPoints[i - 1].Longitude,
                goodPoints[i].Latitude, goodPoints[i].Longitude);
            totalDist += dist;

            var seconds = (goodPoints[i].Timestamp - goodPoints[i - 1].Timestamp).TotalSeconds;
            if (seconds > 0)
            {
                var speedKmh = (dist / seconds) * 3.6;
                if (speedKmh > 50) highSpeedCount++;
            }
        }

        var totalSeconds = (goodPoints[^1].Timestamp - goodPoints[0].Timestamp).TotalSeconds;
        if (totalSeconds <= 0) return false;

        var avgSpeedKmh = (totalDist / totalSeconds) * 3.6;

        // Average speed must be in cycling range
        if (avgSpeedKmh < _prefs.MinConfirmSpeedKmh || avgSpeedKmh > 45) return false;

        // No more than 2 consecutive high-speed readings (vehicle detection)
        if (highSpeedCount > 2) return false;

        return true;
    }

    private void TransitionToRecording()
    {
        // Move confirmation buffer points into the main recording
        _points.Clear();
        _distanceMeters = 0;
        _currentSpeedKmh = 0;

        foreach (var point in _confirmBuffer.Where(p => p.Accuracy <= MaxAccuracyMeters))
        {
            AddRecordingPoint(point);
        }
        _confirmBuffer.Clear();

        _recordingStartTime = _points.Count > 0 ? _points[0].Timestamp : DateTimeOffset.UtcNow;
        _locationService.LocationUpdated += OnLocationUpdated;

        StartPlatformForegroundService("Auto-recording ride...");
        SetState(AutoDetectionState.Recording);
        StatusMessage?.Invoke("Ride confirmed — recording automatically");
    }

    private async Task BeginFinalizingAsync()
    {
        SetState(AutoDetectionState.Finalizing);
        StatusMessage?.Invoke("Ride may have ended, waiting...");

        _finalizeCts?.Cancel();
        _finalizeCts = new CancellationTokenSource();
        var ct = _finalizeCts.Token;

        try
        {
            await Task.Delay(FinalizingGracePeriod, ct);
        }
        catch (OperationCanceledException)
        {
            return; // Finalization was cancelled (cycling resumed)
        }

        // Grace period expired — end the ride
        _locationService.LocationUpdated -= OnLocationUpdated;
        await StopLocationTracking();
        await SaveAndUploadRideAsync();
        SetState(AutoDetectionState.Idle);
    }

    private async Task SaveAndUploadRideAsync()
    {
        if (_points.Count < 10)
        {
            StatusMessage?.Invoke("Ride too short to save");
            _points.Clear();
            _distanceMeters = 0;
            return;
        }

        var ride = new RideRecord
        {
            StartTime = _points[0].Timestamp,
            EndTime = _points[^1].Timestamp,
            DistanceMeters = _distanceMeters,
            Duration = _points[^1].Timestamp - _points[0].Timestamp,
            TrackPointsJson = JsonSerializer.Serialize(_points),
            IsUploaded = false,
            Source = "auto"
        };

        var processed = _postProcessor.Process(ride, new List<TrackPoint>(_points));
        _points.Clear();
        _distanceMeters = 0;

        if (processed == null)
        {
            StatusMessage?.Invoke("Auto-recorded ride didn't meet quality thresholds");
            return;
        }

        await _db.SaveRideAsync(processed);
        var distKm = processed.DistanceMeters / 1000.0;
        var dur = processed.Duration.ToString(@"mm\:ss");
        StatusMessage?.Invoke($"Bike ride saved: {distKm:F1} km in {dur}");

        _ = _upload.UploadPendingAsync();
    }

    private void OnConfirmLocationUpdated(TrackPoint point)
    {
        _confirmBuffer.Add(point);
    }

    private void OnLocationUpdated(TrackPoint point)
    {
        if (State != AutoDetectionState.Recording && State != AutoDetectionState.Finalizing) return;

        // Safety limit
        if (Duration > MaxRecordingDuration)
        {
            _ = BeginFinalizingAsync();
            return;
        }

        AddRecordingPoint(point);
    }

    private void AddRecordingPoint(TrackPoint point)
    {
        if (point.Accuracy > MaxAccuracyMeters) return;

        if (_points.Count > 0 && point.Timestamp <= _points[^1].Timestamp) return;

        if (_points.Count > 0)
        {
            var last = _points[^1];
            var dist = RidePostProcessor.HaversineDistance(
                last.Latitude, last.Longitude, point.Latitude, point.Longitude);

            if (dist < MinDistanceBetweenPointsMeters) return;

            var seconds = (point.Timestamp - last.Timestamp).TotalSeconds;
            if (seconds > 0)
            {
                var impliedSpeedKmh = (dist / seconds) * 3.6;
                if (impliedSpeedKmh > MaxSpeedKmh) return;
                _currentSpeedKmh = impliedSpeedKmh;
            }

            _distanceMeters += dist;
        }

        _points.Add(point);
    }

    private async Task StopLocationTracking()
    {
        if (_locationService.IsTracking)
            await _locationService.StopAsync();
    }

    private void SetState(AutoDetectionState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(newState);
    }

    private static void StartPlatformForegroundService(string notificationText)
    {
#if ANDROID
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(context,
            typeof(Platforms.Android.Services.LocationForegroundService));
        intent.PutExtra("notification_text", notificationText);
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
#endif
    }

    public void Dispose()
    {
        _confirmCts?.Cancel();
        _confirmCts?.Dispose();
        _finalizeCts?.Cancel();
        _finalizeCts?.Dispose();
        _activityService.TransitionDetected -= OnActivityTransition;
        _locationService.LocationUpdated -= OnConfirmLocationUpdated;
        _locationService.LocationUpdated -= OnLocationUpdated;
    }
}
