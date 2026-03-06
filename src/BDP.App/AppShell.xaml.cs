using BDP.App.Views;

namespace BDP.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("rideDetail", typeof(RideDetailPage));
    }
}
