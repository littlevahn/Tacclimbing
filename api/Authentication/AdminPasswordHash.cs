using System.Globalization;
using System.Security.Cryptography;

namespace Tacc.Api.Authentication;

public sealed class AdminPasswordHash
{
    public const int Iterations = 600_000;
    public const int MaximumPasswordLength = 256;
    private readonly byte[] salt;
    private readonly byte[] hash;

    private AdminPasswordHash(byte[] salt, byte[] hash) => (this.salt, this.hash) = (salt, hash);

    public static bool TryParse(string? encoded, out AdminPasswordHash? passwordHash)
    {
        passwordHash = null;
        if (encoded is null || encoded.Length > 512) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "v1" || parts[1] != "pbkdf2-sha256" ||
            parts[2] != Iterations.ToString(CultureInfo.InvariantCulture)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var hash = Convert.FromBase64String(parts[4]);
            if (salt.Length is < 16 or > 64 || hash.Length != 32) return false;
            passwordHash = new AdminPasswordHash(salt, hash);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public bool Verify(string password)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return CryptographicOperations.FixedTimeEquals(actual, hash); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }
}
