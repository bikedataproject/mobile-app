using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

[QueryProperty(nameof(RideId), "rideId")]
public partial class RideDetailViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IGpxSerializer _gpx;
    private readonly IApiService _api;

    [ObservableProperty]
    private int _rideId;

    [ObservableProperty]
    private RideRecord? _ride;

    [ObservableProperty]
    private List<TrackPoint> _trackPoints = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public RideDetailViewModel(IDatabaseService db, IGpxSerializer gpx, IApiService api)
    {
        _db = db;
        _gpx = gpx;
        _api = api;
    }

    [RelayCommand]
    private async Task LoadRideAsync()
    {
        Ride = await _db.GetRideAsync(RideId);
        if (Ride is null) return;

        TrackPoints = JsonSerializer.Deserialize<List<TrackPoint>>(Ride.TrackPointsJson) ?? [];
    }

    [RelayCommand]
    private async Task UploadRideAsync()
    {
        if (Ride is null || Ride.IsUploaded) return;

        IsBusy = true;
        StatusMessage = "Uploading...";

        var points = JsonSerializer.Deserialize<List<TrackPoint>>(Ride.TrackPointsJson) ?? [];
        var gpxXml = _gpx.Serialize(points, Ride.StartTime);
        var fileName = $"ride_{Ride.StartTime:yyyyMMdd_HHmmss}.gpx";

        var result = await _api.UploadGpxAsync(gpxXml, fileName);
        if (result.Errors.Count > 0)
        {
            StatusMessage = $"Upload failed: {string.Join(", ", result.Errors)}";
        }
        else if (result.Imported > 0)
        {
            await _db.MarkUploadedAsync(Ride.Id);
            Ride.IsUploaded = true;
            OnPropertyChanged(nameof(Ride));
            StatusMessage = "Uploaded successfully!";
        }
        else if (result.Duplicates > 0)
        {
            await _db.MarkUploadedAsync(Ride.Id);
            Ride.IsUploaded = true;
            OnPropertyChanged(nameof(Ride));
            StatusMessage = "Already uploaded (duplicate).";
        }
        else
        {
            StatusMessage = "Upload failed: server returned no imports, duplicates, or errors.";
        }

        IsBusy = false;
    }
}
