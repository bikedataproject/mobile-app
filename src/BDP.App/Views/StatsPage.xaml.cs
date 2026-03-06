using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class StatsPage : ContentPage
{
    public StatsPage(StatsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is StatsViewModel vm)
            await vm.LoadStatsCommand.ExecuteAsync(null);
    }
}
