using BDP.App.Services;

namespace BDP.App;

public partial class App : Application
{
    public App(IAuthService auth)
    {
        InitializeComponent();

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
