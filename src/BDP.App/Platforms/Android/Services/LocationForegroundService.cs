using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace BDP.App.Platforms.Android.Services;

[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeLocation)]
public class LocationForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "bdp_location_channel";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("Bike Data Project");
        builder.SetContentText("Recording ride...");
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetOngoing(true);
        builder.SetCategory(NotificationCompat.CategoryService);
        var notification = builder.Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "Location Tracking", NotificationImportance.Low)
        {
            Description = "Shows when a bike ride is being recorded"
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
