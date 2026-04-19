using Android.Content;
using Android.Gms.Location;
using BDP.App.Models;

namespace BDP.App.Platforms.Android.Receivers;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ActivityTransitionReceiver : BroadcastReceiver
{
    public const string IntentAction = "org.bikedataproject.app.ACTIVITY_TRANSITION";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent == null) return;

        if (ActivityTransitionResult.HasResult(intent))
        {
            var result = ActivityTransitionResult.ExtractResult(intent);
            if (result == null) return;

            foreach (var ev in result.TransitionEvents)
            {
                var activityType = ev.ActivityType switch
                {
                    DetectedActivity.OnBicycle => DetectedActivityType.OnBicycle,
                    DetectedActivity.OnFoot => DetectedActivityType.OnFoot,
                    DetectedActivity.Still => DetectedActivityType.Still,
                    DetectedActivity.InVehicle => DetectedActivityType.InVehicle,
                    DetectedActivity.Running => DetectedActivityType.Running,
                    DetectedActivity.Walking => DetectedActivityType.Walking,
                    _ => DetectedActivityType.Unknown
                };

                var transition = ev.TransitionType == ActivityTransition.ActivityTransitionEnter
                    ? TransitionType.Enter
                    : TransitionType.Exit;

                ActivityTransitionBridge.OnTransition(new ActivityTransitionData(
                    activityType, transition, DateTimeOffset.UtcNow));
            }
        }
    }
}
