namespace BlazorBlobExplorer.Services;

public class SwaAuthResponse
{
    public SwaClientPrincipal? ClientPrincipal { get; set; }
}

public class SwaClientPrincipal
{
    public string? UserId { get; set; }
    public string? UserDetails { get; set; }
    public string? IdentityProvider { get; set; }
    public string[]? UserRoles { get; set; }
    public SwaAuthClaim[]? Claims { get; set; }
}

public class SwaAuthClaim
{
    public string? Typ { get; set; }
    public string? Val { get; set; }
}
