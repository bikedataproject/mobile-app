using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class RideDetailPage : ContentPage
{
    public RideDetailPage(RideDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RideDetailViewModel vm)
            await vm.LoadRideCommand.ExecuteAsync(null);
    }
}
