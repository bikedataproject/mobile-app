namespace BDP.App.Services;

public interface IAuthService
{
    bool IsLoggedIn { get; }
    string? AccessToken { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? LastError { get; }

    Task<bool> LoginAsync();
    Task LogoutAsync();
    Task<bool> TryRestoreSessionAsync();
    Task<string?> GetValidTokenAsync();
}
