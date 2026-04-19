namespace BDP.App.Models;

public enum DetectedActivityType
{
    Unknown,
    OnBicycle,
    OnFoot,
    Still,
    InVehicle,
    Running,
    Walking
}

public enum TransitionType
{
    Enter,
    Exit
}

public sealed record ActivityTransitionData(
    DetectedActivityType ActivityType,
    TransitionType Transition,
    DateTimeOffset Timestamp);
