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

    public async Task<UploadResult> UploadGpxAsync(string gpxContent, string fileName)
    {
        var token = await _auth.GetValidTokenAsync();
        if (token is null)
            return new UploadResult { Failed = 1, Errors = ["Authentication failed - no valid token. Try logging out and back in."] };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/manual/upload?source=mobile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");

        var form = new MultipartFormDataContent();
        form.Add(fileContent, "files", fileName);
        request.Content = form;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            return new UploadResult { Failed = 1, Errors = [$"Network error: {ex.Message}"] };
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _auth.LogoutAsync();
            return new UploadResult { Failed = 1, Errors = ["Session expired. Please log in again."] };
        }

        if (!response.IsSuccessStatusCode)
            return new UploadResult { Failed = 1, Errors = [$"HTTP {(int)response.StatusCode}: {responseBody}"] };

        try
        {
            var result = JsonSerializer.Deserialize<UploadResult>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return result ?? new UploadResult { Failed = 1, Errors = [$"Empty response from server. Body: {responseBody}"] };
        }
        catch (JsonException ex)
        {
            return new UploadResult { Failed = 1, Errors = [$"Unexpected response format: {responseBody} ({ex.Message})"] };
        }
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
