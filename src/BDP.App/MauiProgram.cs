using BDP.App.Services;
using BDP.App.ViewModels;
using BDP.App.Views;
using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Hosting;
#if ANDROID
using BDP.App.Platforms.Android.Services;
#endif

namespace BDP.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<IGpxSerializer, GpxSerializer>();
        builder.Services.AddSingleton<IApiService, ApiService>();
        builder.Services.AddSingleton<ILocationService, LocationService>();
        builder.Services.AddSingleton<IRideTracker, RideTracker>();
        builder.Services.AddSingleton<UploadService>();
        builder.Services.AddSingleton<PreferencesService>();
        builder.Services.AddSingleton<RidePostProcessor>();
        builder.Services.AddSingleton<AutoRideDetectionService>();

#if ANDROID
        builder.Services.AddSingleton<IActivityRecognitionService>(sp =>
        {
            var service = new BDP.App.Platforms.Android.Services.ActivityRecognitionService();
            return service.IsAvailable
                ? service
                : new BDP.App.Platforms.Android.Services.NoOpActivityRecognitionService();
        });
#else
        builder.Services.AddSingleton<IActivityRecognitionService, NoOpActivityRecognitionService>();
#endif

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddSingleton<RecordViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddSingleton<RecordPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
