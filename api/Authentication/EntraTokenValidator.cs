using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Tacc.Api.Authentication;

public interface IEntraTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

public sealed class EntraTokenValidator(
    IOptions<EntraAuthenticationOptions> options,
    ILogger<EntraTokenValidator> logger) : IEntraTokenValidator
{
    private readonly EntraAuthenticationOptions options = options.Value;
    private readonly JwtSecurityTokenHandler tokenHandler = new();
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? configurationManager =
        CreateConfigurationManager(options.Value.Authority);

    public async Task<ClaimsPrincipal?> ValidateAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (!TryGetBearerToken(authorizationHeader, out var token) ||
            configurationManager is null ||
            string.IsNullOrWhiteSpace(options.Audience ?? options.ClientId))
        {
            return null;
        }

        try
        {
            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken);
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateIssuer = true,
                ValidIssuer = configuration.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience ?? options.ClientId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                RoleClaimType = "roles",
                NameClaimType = "name"
            };

            return tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch (SecurityTokenException exception)
        {
            logger.LogWarning(exception, "An admin request supplied an invalid bearer token.");
            return null;
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "An admin request supplied a malformed bearer token.");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Entra token validation could not be completed.");
            return null;
        }
    }

    private static ConfigurationManager<OpenIdConnectConfiguration>? CreateConfigurationManager(
        string? authority)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            authority!.Contains("<not-configured>", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metadataAddress = $"{authorityUri.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration";
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    private static bool TryGetBearerToken(string? authorizationHeader, out string token)
    {
        token = string.Empty;
        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorizationHeader[bearerPrefix.Length..].Trim();
        return token.Length > 0;
    }
}
