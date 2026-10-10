namespace Tacc.Api.Authentication;

public sealed class AdminAuthenticationOptions
{
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public string? SessionSecret { get; set; }
}
