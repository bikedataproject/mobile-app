using BDP.App.Models;

namespace BDP.App.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<int> SaveRideAsync(RideRecord ride);
    Task<RideRecord?> GetRideAsync(int id);
    Task<List<RideRecord>> GetAllRidesAsync();
    Task<List<RideRecord>> GetPendingUploadsAsync();
    Task MarkUploadedAsync(int id);
    Task DeleteRideAsync(int id);
}
