namespace BDP.App.Services;

public sealed class PreferencesService
{
    private const string AutoDetectionEnabledKey = "auto_detection_enabled";
    private const string AutoDetectionConsentedKey = "auto_detection_consented";
    private const string SensitivityKey = "auto_detection_sensitivity";
    private const string MinRideDistanceKey = "min_ride_distance_meters";

    public bool AutoDetectionEnabled
    {
        get => Preferences.Default.Get(AutoDetectionEnabledKey, false);
        set => Preferences.Default.Set(AutoDetectionEnabledKey, value);
    }

    public bool AutoDetectionConsented
    {
        get => Preferences.Default.Get(AutoDetectionConsentedKey, false);
        set => Preferences.Default.Set(AutoDetectionConsentedKey, value);
    }

    /// <summary>"standard" or "aggressive"</summary>
    public string Sensitivity
    {
        get => Preferences.Default.Get(SensitivityKey, "standard");
        set => Preferences.Default.Set(SensitivityKey, value);
    }

    public int MinRideDistanceMeters
    {
        get => Preferences.Default.Get(MinRideDistanceKey, 200);
        set => Preferences.Default.Set(MinRideDistanceKey, value);
    }

    // Derived confirmation parameters
    public TimeSpan ConfirmationWindow => Sensitivity == "aggressive"
        ? TimeSpan.FromSeconds(45)
        : TimeSpan.FromSeconds(90);

    public double MinConfirmSpeedKmh => Sensitivity == "aggressive" ? 3.0 : 5.0;
}
