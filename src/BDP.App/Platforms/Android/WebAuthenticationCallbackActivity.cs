using Android.App;
using Android.Content;
using Android.Content.PM;

namespace BDP.App;

[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "org.bikedataproject.app",
    DataHost = "callback")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
