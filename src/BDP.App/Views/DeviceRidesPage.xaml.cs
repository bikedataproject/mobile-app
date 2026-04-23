using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class DeviceRidesPage : ContentPage
{
    private readonly DeviceRidesViewModel _vm;

    public DeviceRidesPage(DeviceRidesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public async Task LoadAsync(int trackerId)
    {
        await _vm.LoadRidesAsync(trackerId);
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//tracker");
    }
}
