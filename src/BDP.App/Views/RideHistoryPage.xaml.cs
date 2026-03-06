using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class RideHistoryPage : ContentPage
{
    public RideHistoryPage(RideHistoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RideHistoryViewModel vm)
            await vm.LoadRidesCommand.ExecuteAsync(null);
    }
}
