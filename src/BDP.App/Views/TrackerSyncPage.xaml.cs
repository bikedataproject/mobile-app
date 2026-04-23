using BDP.App.Services;
using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class TrackerSyncPage : ContentPage
{
    private readonly TrackerSyncViewModel _vm;

    public TrackerSyncPage(TrackerSyncViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.OnPageOpenAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.OnPageClose();
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//record");
    }

    private async void OnDeviceTapped(object? sender, EventArgs e)
    {
        var page = Handler?.MauiContext?.Services.GetService<DeviceRidesPage>();
        if (page is null) return;

        var tracker = await Handler?.MauiContext?.Services.GetService<IDatabaseService>()?.GetTrackerAsync()!;
        if (tracker is null) return;

        await page.LoadAsync(tracker.Id);
        await Navigation.PushAsync(page);
    }
}
