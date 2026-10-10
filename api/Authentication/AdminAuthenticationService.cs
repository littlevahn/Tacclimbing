using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Tacc.Api.Authentication;

public sealed record AdminSession(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class AdminAuthenticationService
{
    public const string Issuer = "tacc-admin";
    public const string Audience = "tacc-inventory-api";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly string? username;
    private readonly AdminPasswordHash? passwordHash;
    private readonly SymmetricSecurityKey? signingKey;
    private readonly TimeProvider clock;
    private readonly JwtSecurityTokenHandler tokenHandler = new() { MapInboundClaims = false };
    public bool IsConfigured { get; }

    public AdminAuthenticationService(IOptions<AdminAuthenticationOptions> options,
        TimeProvider clock, ILogger<AdminAuthenticationService> logger)
    {
        this.clock = clock;
        var settings = options.Value;
        username = settings.Username;
        var validUsername = !string.IsNullOrWhiteSpace(username) && username.Length <= 128 &&
            username == username.Trim() && !username.Any(char.IsControl);
        var validHash = AdminPasswordHash.TryParse(settings.PasswordHash, out passwordHash);
        byte[]? keyBytes = null;
        try
        {
            if (settings.SessionSecret?.Length <= 128)
                keyBytes = Convert.FromBase64String(settings.SessionSecret);
        }
        catch (FormatException) { }
        if (keyBytes?.Length == 32) signingKey = new SymmetricSecurityKey(keyBytes);
        IsConfigured = validUsername && validHash && signingKey is not null;
        if (!IsConfigured)
            logger.LogError("Administrator authentication configuration is missing or invalid. Admin access is disabled.");
    }

    public bool VerifyCredentials(string suppliedUsername, string password)
    {
        if (!IsConfigured || password.Length is 0 or > AdminPasswordHash.MaximumPasswordLength) return false;
        // Always perform the expensive comparison, including for an unknown username.
        var validPassword = passwordHash!.Verify(password);
        return validPassword & string.Equals(suppliedUsername, username, StringComparison.Ordinal);
    }

    public AdminSession IssueSession()
    {
        if (!IsConfigured) throw new InvalidOperationException("Administrator authentication is unavailable.");
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var expires = now.Add(SessionLifetime);
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, username!),
             new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)],
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(signingKey!, SecurityAlgorithms.HmacSha256));
        return new AdminSession(tokenHandler.WriteToken(token), expires);
    }

    public ClaimsPrincipal? ValidateBearer(string? authorization)
    {
        const string prefix = "Bearer ";
        if (!IsConfigured || authorization is null || authorization.Length > 4096 ||
            !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[prefix.Length..].Trim();
        if (token.Length == 0) return null;
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = Audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                NameClaimType = JwtRegisteredClaimNames.Sub,
                LifetimeValidator = (notBefore, expires, _, _) =>
                    expires.HasValue && expires.Value > clock.GetUtcNow().UtcDateTime &&
                    notBefore.HasValue && notBefore.Value <= clock.GetUtcNow().UtcDateTime
            }, out var validated);
            if (validated is not JwtSecurityToken jwt || jwt.Header.Alg != SecurityAlgorithms.HmacSha256 ||
                jwt.ValidTo - jwt.IssuedAt != SessionLifetime ||
                principal.FindAll(JwtRegisteredClaimNames.Sub).Count() != 1 ||
                !string.Equals(principal.Identity?.Name, username, StringComparison.Ordinal)) return null;
            return principal;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            // Never log the supplied token, validation exception, or credentials.
            return null;
        }
    }
}
