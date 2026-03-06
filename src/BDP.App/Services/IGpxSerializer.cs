using BDP.App.Models;

namespace BDP.App.Services;

public interface IGpxSerializer
{
    string Serialize(IReadOnlyList<TrackPoint> points, DateTimeOffset startTime);
}
