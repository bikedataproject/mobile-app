using Android.App;
using Android.Content;

namespace BDP.App.Platforms.Android.Receivers;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = false)]
[IntentFilter([Intent.ActionBootCompleted])]
public class BootCompletedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;

        // Re-register Activity Recognition after reboot by starting the foreground service.
        // The actual re-registration happens when AutoRideDetectionService is initialized
        // on app launch — we just need the app process to start.
        // On modern Android, BOOT_COMPLETED alone can't start foreground services directly,
        // but it ensures the process is created so the next user interaction triggers full setup.
        System.Diagnostics.Debug.WriteLine("BootCompletedReceiver: device rebooted, app process created.");
    }
}
