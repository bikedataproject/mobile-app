using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BDP.App.Services;

public sealed class AuthService : IAuthService
{
    private const string Authority = "https://www.bikedataproject.org/api/users/realms/bdp";
    private const string ClientId = "mobile-app";
    private const string CallbackUri = "org.bikedataproject.app://callback";
    private const string Scopes = "openid profile email";

    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry;

    public bool IsLoggedIn => _accessToken is not null;
    public string? AccessToken => _accessToken;
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }

    public string? LastError { get; private set; }

    public async Task<bool> LoginAsync()
    {
        try
        {
            LastError = null;
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            var authorizeUrl = $"{Authority}/protocol/openid-connect/auth" +
                $"?client_id={Uri.EscapeDataString(ClientId)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(Scopes)}" +
                $"&redirect_uri={Uri.EscapeDataString(CallbackUri)}" +
                $"&code_challenge={codeChallenge}" +
                $"&code_challenge_method=S256";

            var result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authorizeUrl),
                new Uri(CallbackUri));

            if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                LastError = $"No auth code in callback. Keys: {string.Join(", ", result.Properties.Keys)}";
                return false;
            }

            return await ExchangeCodeAsync(code, codeVerifier);
        }
        catch (TaskCanceledException)
        {
            LastError = "Login was cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        _accessToken = null;
        UserId = null;
        UserName = null;
        _tokenExpiry = DateTimeOffset.MinValue;

        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);

        await Task.CompletedTask;
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken)) return false;

        return await RefreshTokenAsync(refreshToken);
    }

    public async Task<string?> GetValidTokenAsync()
    {
        if (_accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var success = await RefreshTokenAsync(refreshToken);
        return success ? _accessToken : null;
    }

    private async Task<bool> ExchangeCodeAsync(string code, string codeVerifier)
    {
        using var client = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["redirect_uri"] = CallbackUri,
            ["code_verifier"] = codeVerifier
        });

        var response = await client.PostAsync($"{Authority}/protocol/openid-connect/token", content);
        if (!response.IsSuccessStatusCode) return false;

        var json = await response.Content.ReadAsStringAsync();
        return ParseTokenResponse(json);
    }

    private async Task<bool> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken
            });

            var response = await client.PostAsync($"{Authority}/protocol/openid-connect/token", content);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            return ParseTokenResponse(json);
        }
        catch
        {
            return false;
        }
    }

    private bool ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var at)) return false;

        _accessToken = at.GetString();
        if (_accessToken is null) return false;

        if (root.TryGetProperty("expires_in", out var exp))
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32());

        if (root.TryGetProperty("refresh_token", out var rt))
            SecureStorage.Default.SetAsync(RefreshTokenKey, rt.GetString()!).FireAndForget();

        SecureStorage.Default.SetAsync(AccessTokenKey, _accessToken).FireAndForget();

        ParseClaims(_accessToken);
        return true;
    }

    private void ParseClaims(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return;

        var jwt = handler.ReadJwtToken(token);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var userId))
            UserId = userId;

        UserName = jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username" || c.Type == "name")?.Value;
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal static class TaskExtensions
{
    public static void FireAndForget(this Task task)
    {
        task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
