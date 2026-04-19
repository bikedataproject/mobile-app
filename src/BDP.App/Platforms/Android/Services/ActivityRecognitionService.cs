using Android.App;
using Android.Content;
using Android.Gms.Extensions;
using Android.Gms.Location;
using BDP.App.Models;
using BDP.App.Platforms.Android.Receivers;

namespace BDP.App.Platforms.Android.Services;

public sealed class ActivityRecognitionService : BDP.App.Services.IActivityRecognitionService, IDisposable
{
    private IActivityRecognitionClient? _client;
    private PendingIntent? _pendingIntent;

    public bool IsAvailable { get; private set; }
    public bool IsMonitoring { get; private set; }

    public event Action<ActivityTransitionData>? TransitionDetected;

    public ActivityRecognitionService()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            _client = ActivityRecognition.GetClient(context);
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task<bool> StartMonitoringAsync()
    {
        if (!IsAvailable || _client == null) return false;
        if (IsMonitoring) return true;

        // Check ACTIVITY_RECOGNITION permission (required on API 29+)
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Sensors>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Sensors>();
                if (status != PermissionStatus.Granted)
                    return false;
            }
        }

        var context = global::Android.App.Application.Context;

        var intent = new Intent(context, typeof(ActivityTransitionReceiver));
        intent.SetAction(ActivityTransitionReceiver.IntentAction);

        _pendingIntent = PendingIntent.GetBroadcast(
            context, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);

        var transitions = new List<ActivityTransition>
        {
            new ActivityTransition.Builder()
                .SetActivityType(DetectedActivity.OnBicycle)
                .SetActivityTransition(ActivityTransition.ActivityTransitionEnter)
                .Build(),
            new ActivityTransition.Builder()
                .SetActivityType(DetectedActivity.OnBicycle)
                .SetActivityTransition(ActivityTransition.ActivityTransitionExit)
                .Build(),
            new ActivityTransition.Builder()
                .SetActivityType(DetectedActivity.Still)
                .SetActivityTransition(ActivityTransition.ActivityTransitionEnter)
                .Build(),
            new ActivityTransition.Builder()
                .SetActivityType(DetectedActivity.OnFoot)
                .SetActivityTransition(ActivityTransition.ActivityTransitionEnter)
                .Build(),
            new ActivityTransition.Builder()
                .SetActivityType(DetectedActivity.InVehicle)
                .SetActivityTransition(ActivityTransition.ActivityTransitionEnter)
                .Build(),
        };

        var request = new ActivityTransitionRequest(transitions);

        try
        {
            await _client.RequestActivityTransitionUpdates(request, _pendingIntent);

            ActivityTransitionBridge.TransitionReceived += OnBridgeTransition;
            IsMonitoring = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Activity Recognition start failed: {ex.Message}");
            return false;
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (!IsMonitoring || _client == null || _pendingIntent == null) return;

        ActivityTransitionBridge.TransitionReceived -= OnBridgeTransition;

        try
        {
            await _client.RemoveActivityTransitionUpdates(_pendingIntent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Activity Recognition stop failed: {ex.Message}");
        }

        IsMonitoring = false;
    }

    private void OnBridgeTransition(ActivityTransitionData data)
    {
        TransitionDetected?.Invoke(data);
    }

    public void Dispose()
    {
        ActivityTransitionBridge.TransitionReceived -= OnBridgeTransition;
    }
}
