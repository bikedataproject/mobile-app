using BDP.App.Models;

namespace BDP.App.Platforms.Android.Services;

/// <summary>
/// Fallback when Google Play Services is unavailable.
/// </summary>
public sealed class NoOpActivityRecognitionService : BDP.App.Services.IActivityRecognitionService
{
    public bool IsAvailable => false;
    public bool IsMonitoring => false;

    public event Action<ActivityTransitionData>? TransitionDetected;

    public Task<bool> StartMonitoringAsync() => Task.FromResult(false);
    public Task StopMonitoringAsync() => Task.CompletedTask;
}
