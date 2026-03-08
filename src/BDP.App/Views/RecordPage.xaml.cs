using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class RecordPage : ContentPage
{
    private readonly RecordViewModel _vm;

    public RecordPage(RecordViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartTimer(Dispatcher);
    }

    private async void OnStatsLinkTapped(object? sender, EventArgs e)
    {
        await Launcher.Default.OpenAsync("https://www.bikedataproject.org/share-data");
    }
}
