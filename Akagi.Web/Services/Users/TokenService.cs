using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Akagi.Web.Services.Users;

public interface ITokenService
{
    Task<string?> GetGoogleIdTokenAsync();
}

public class TokenService : ITokenService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string?> GetGoogleIdTokenAsync()
    {
        if (_httpContextAccessor.HttpContext == null || !_httpContextAccessor.HttpContext.User.Identity!.IsAuthenticated)
            return null;

        AuthenticateResult authResult = await _httpContextAccessor.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authResult.Succeeded)
        {
            return authResult.Properties.GetTokenValue("access_token");
        }

        return null;
    }

    public async Task<IDictionary<string, string?>> GetAllAvailableTokensAsync()
    {
        Dictionary<string, string?> result = [];

        if (_httpContextAccessor.HttpContext == null || !_httpContextAccessor.HttpContext.User.Identity!.IsAuthenticated)
            return result;

        AuthenticateResult authResult = await _httpContextAccessor.HttpContext.AuthenticateAsync();
        if (authResult.Succeeded)
        {
            IEnumerable<AuthenticationToken> tokens = authResult.Properties.GetTokens();
            foreach (AuthenticationToken token in tokens)
            {
                result[token.Name] = token.Value;
            }
        }

        return result;
    }
}