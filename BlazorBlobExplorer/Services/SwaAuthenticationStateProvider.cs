using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using System.Security.Claims;

namespace BlazorBlobExplorer.Services;

public class SwaAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SwaAuthenticationStateProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var http = _httpClientFactory.CreateClient("SwaAuth");
            var response = await http.GetFromJsonAsync<SwaAuthResponse>("/.auth/me");
            var principal = response?.ClientPrincipal;

            if (principal == null || string.IsNullOrEmpty(principal.UserId))
                return Anonymous();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, principal.UserId),
                new Claim(ClaimTypes.Name, principal.UserDetails ?? principal.UserId),
                new Claim("identityProvider", principal.IdentityProvider ?? "")
            };

            foreach (var role in principal.UserRoles ?? Array.Empty<string>())
                claims.Add(new Claim(ClaimTypes.Role, role));

            var identity = new ClaimsIdentity(claims, principal.IdentityProvider);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Anonymous();
        }
    }

    private static AuthenticationState Anonymous() =>
        new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

    public void NotifyAuthStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
