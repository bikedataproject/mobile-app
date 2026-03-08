using BDP.App.Services;
using BDP.App.ViewModels;
using BDP.App.Views;
using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Hosting;

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

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddSingleton<RecordViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddSingleton<RecordPage>();

        return builder.Build();
    }
}
