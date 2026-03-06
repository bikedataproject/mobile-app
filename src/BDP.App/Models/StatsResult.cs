using System.Text.Json.Serialization;

namespace BDP.App.Models;

public sealed class StatsResult
{
    [JsonPropertyName("totalTracks")]
    public int TotalTracks { get; set; }

    [JsonPropertyName("totalDistanceKm")]
    public double TotalDistanceKm { get; set; }

    [JsonPropertyName("byProvider")]
    public List<ProviderStats> ByProvider { get; set; } = [];
}

public sealed class ProviderStats
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("tracks")]
    public int Tracks { get; set; }

    [JsonPropertyName("distanceKm")]
    public double DistanceKm { get; set; }
}
