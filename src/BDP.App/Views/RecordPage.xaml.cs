using BDP.App.Services;
using BDP.App.ViewModels;

namespace BDP.App.Views;

public partial class RecordPage : ContentPage
{
    private readonly RecordViewModel _vm;

    public RecordPage(RecordViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordViewModel.State))
                UpdateButtonVisibility();
        };

        UpdateButtonVisibility();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartTimer(Dispatcher);
    }

    private void UpdateButtonVisibility()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var state = _vm.State;
            StartButton.IsVisible = state == RideState.Idle;
            PauseButton.IsVisible = state == RideState.Recording;
            StopButton.IsVisible = state is RideState.Recording or RideState.Paused;
            ResumeButton.IsVisible = state == RideState.Paused;
        });
    }
}
