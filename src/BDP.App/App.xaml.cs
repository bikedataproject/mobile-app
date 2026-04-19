using BDP.App.Services;

namespace BDP.App;

public partial class App : Application
{
    private readonly UploadService _upload;
    private readonly AutoRideDetectionService _autoDetection;
    private readonly PreferencesService _prefs;

    public App(IAuthService auth, UploadService upload, AutoRideDetectionService autoDetection, PreferencesService prefs)
    {
        InitializeComponent();
        _upload = upload;
        _autoDetection = autoDetection;
        _prefs = prefs;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"FATAL: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"UNOBSERVED: {e.Exception}");
            e.SetObserved();
        };

        auth.SessionExpired += OnSessionExpired;

        // Start periodic upload retry (also uploads any rides that failed previously)
        _upload.Start();

        // Start auto-detection if previously enabled
        if (_prefs.AutoDetectionEnabled)
        {
            _ = _autoDetection.EnableAsync();
        }
    }

    private async void OnSessionExpired(object? sender, EventArgs e)
    {
        if (MainThread.IsMainThread)
        {
            await Shell.Current.DisplayAlertAsync("Session Expired", "Your session has expired. Please log in again.", "OK");
            await Shell.Current.GoToAsync("//login");
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.DisplayAlertAsync("Session Expired", "Your session has expired. Please log in again.", "OK");
                await Shell.Current.GoToAsync("//login");
            });
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
