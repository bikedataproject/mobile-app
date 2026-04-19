using BDP.App.Models;

namespace BDP.App.Platforms.Android;

/// <summary>
/// Static bridge that forwards activity transition events from the BroadcastReceiver
/// (which Android instantiates outside DI) to the DI-managed service.
/// </summary>
public static class ActivityTransitionBridge
{
    public static event Action<ActivityTransitionData>? TransitionReceived;

    public static void OnTransition(ActivityTransitionData data)
    {
        TransitionReceived?.Invoke(data);
    }
}
