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

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddSingleton<RecordViewModel>();
        builder.Services.AddTransient<RideDetailViewModel>();
        builder.Services.AddTransient<RideHistoryViewModel>();
        builder.Services.AddTransient<StatsViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddSingleton<RecordPage>();
        builder.Services.AddTransient<RideDetailPage>();
        builder.Services.AddTransient<RideHistoryPage>();
        builder.Services.AddTransient<StatsPage>();

        return builder.Build();
    }
}
