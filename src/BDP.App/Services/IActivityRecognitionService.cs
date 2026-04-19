using BDP.App.Models;

namespace BDP.App.Services;

public interface IActivityRecognitionService
{
    bool IsAvailable { get; }
    bool IsMonitoring { get; }

    event Action<ActivityTransitionData>? TransitionDetected;

    Task<bool> StartMonitoringAsync();
    Task StopMonitoringAsync();
}
