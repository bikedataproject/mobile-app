using BDP.App.Models;

namespace BDP.App.Services;

public interface IApiService
{
    Task<UploadResult?> UploadGpxAsync(string gpxContent, string fileName);
    Task<StatsResult?> GetStatsAsync();
}
