using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

public sealed class ApiService : IApiService
{
    private const string BaseUrl = "https://www.bikedataproject.org";

    private readonly IAuthService _auth;
    private readonly HttpClient _httpClient;

    public ApiService(IAuthService auth)
    {
        _auth = auth;
        _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public async Task<UploadResult?> UploadGpxAsync(string gpxContent, string fileName)
    {
        var token = await _auth.GetValidTokenAsync();
        if (token is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/manual/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");

        var form = new MultipartFormDataContent();
        form.Add(fileContent, "files", fileName);
        request.Content = form;

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<UploadResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<StatsResult?> GetStatsAsync()
    {
        var token = await _auth.GetValidTokenAsync();
        if (token is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<StatsResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
