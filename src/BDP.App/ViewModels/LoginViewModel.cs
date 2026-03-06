using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _userName;

    public LoginViewModel(IAuthService auth)
    {
        _auth = auth;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = "Starting login...";

        try
        {
            var success = await _auth.LoginAsync();
            if (success)
            {
                UserName = _auth.UserName;
                ErrorMessage = "Login OK, navigating...";
                await Shell.Current.GoToAsync("//record");
            }
            else
            {
                ErrorMessage = _auth.LastError ?? "Login failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task TryAutoLoginAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = "Checking saved session...";
            var restored = await _auth.TryRestoreSessionAsync();
            if (restored)
            {
                UserName = _auth.UserName;
                await Shell.Current.GoToAsync("//record");
            }
            else
            {
                ErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Auto-login: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
