using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly IApiService _api;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _totalTracks;

    [ObservableProperty]
    private double _totalDistanceKm;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<ProviderStats> ByProvider { get; } = [];

    public StatsViewModel(IApiService api)
    {
        _api = api;
    }

    [RelayCommand]
    private async Task LoadStatsAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        var stats = await _api.GetStatsAsync();
        if (stats is not null)
        {
            TotalTracks = stats.TotalTracks;
            TotalDistanceKm = stats.TotalDistanceKm;

            ByProvider.Clear();
            foreach (var p in stats.ByProvider)
                ByProvider.Add(p);
        }
        else
        {
            ErrorMessage = "Could not load stats. Check your connection.";
        }

        IsBusy = false;
    }
}
