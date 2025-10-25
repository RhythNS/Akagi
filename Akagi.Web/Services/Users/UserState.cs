using Akagi.Web.Data;
using Akagi.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace Akagi.Web.Services.Users;

public interface IUserState
{
    public Task<User> GetCurrentUserAsync();
}

public class UserState : IUserState
{
    private readonly AuthenticationStateProvider _authProvider;
    private readonly IUserDatabase _database;
    private User? _currentUser;

    public UserState(AuthenticationStateProvider authProvider, IUserDatabase database)
    {
        _authProvider = authProvider;
        _database = database;
    }

    public async Task<User> GetCurrentUserAsync()
    {
        if (_currentUser != null)
        {
            return _currentUser;
        }

        AuthenticationState authState = await _authProvider.GetAuthenticationStateAsync();
        System.Security.Claims.ClaimsPrincipal? user = authState.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            System.Security.Claims.Claim? internalIdClaim = user.FindFirst("internal_id");
            if (internalIdClaim != null)
            {
                _currentUser = await _database.GetDocumentByIdAsync(internalIdClaim.Value);
                string googleToken = user.FindFirst("access_token")?.Value ?? string.Empty;
                if (_currentUser != null && !string.IsNullOrEmpty(googleToken))
                {
                    _currentUser.GoogleToken = googleToken;
                }
            }
        }

        return _currentUser ?? throw new InvalidOperationException("User is not authenticated or internal_id claim is missing.");
    }
}
