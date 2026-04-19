using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly PreferencesService _prefs;
    private readonly AutoRideDetectionService _autoDetection;
    private readonly IActivityRecognitionService _activityRecognition;

    [ObservableProperty]
    private bool _autoDetectionEnabled;

    [ObservableProperty]
    private bool _autoDetectionAvailable;

    [ObservableProperty]
    private bool _isAggressive;

    [ObservableProperty]
    private int _minRideDistanceMeters;

    [ObservableProperty]
    private string _statusMessage = "";

    public SettingsViewModel(
        PreferencesService prefs,
        AutoRideDetectionService autoDetection,
        IActivityRecognitionService activityRecognition)
    {
        _prefs = prefs;
        _autoDetection = autoDetection;
        _activityRecognition = activityRecognition;

        _autoDetectionAvailable = _activityRecognition.IsAvailable;
        _autoDetectionEnabled = _prefs.AutoDetectionEnabled;
        _isAggressive = _prefs.Sensitivity == "aggressive";
        _minRideDistanceMeters = _prefs.MinRideDistanceMeters;

        if (!_autoDetectionAvailable)
            _statusMessage = "Activity Recognition requires Google Play Services";
    }

    [RelayCommand]
    private async Task ToggleAutoDetectionAsync()
    {
        if (AutoDetectionEnabled)
        {
            // Turning on — show consent if first time
            if (!_prefs.AutoDetectionConsented)
            {
                var consented = await Shell.Current.DisplayAlert(
                    "Enable Auto-Detection",
                    "Auto-detection monitors your activity in the background to detect cycling. " +
                    "Activity data stays on your device until a ride is confirmed. " +
                    "This uses minimal battery while monitoring.\n\n" +
                    "You can disable this at any time.",
                    "Enable",
                    "Cancel");

                if (!consented)
                {
                    AutoDetectionEnabled = false;
                    return;
                }

                _prefs.AutoDetectionConsented = true;
            }

            var success = await _autoDetection.EnableAsync();
            if (!success)
            {
                AutoDetectionEnabled = false;
                StatusMessage = "Could not enable auto-detection";
                return;
            }

            _prefs.AutoDetectionEnabled = true;
            StatusMessage = "Auto-detection is active";
        }
        else
        {
            await _autoDetection.DisableAsync();
            _prefs.AutoDetectionEnabled = false;
            StatusMessage = "Auto-detection disabled";
        }
    }

    partial void OnIsAggressiveChanged(bool value)
    {
        _prefs.Sensitivity = value ? "aggressive" : "standard";
    }

    partial void OnMinRideDistanceMetersChanged(int value)
    {
        // Clamp to valid range
        var clamped = Math.Clamp(value, 100, 1000);
        _prefs.MinRideDistanceMeters = clamped;
    }

    [RelayCommand]
    private async Task OpenBatterySettingsAsync()
    {
        await Launcher.Default.OpenAsync("https://dontkillmyapp.com/");
    }
}
