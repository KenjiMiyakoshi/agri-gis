namespace AgriGis.Api.Auth;

public sealed class PasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string plain)
    {
        if (string.IsNullOrEmpty(plain))
            throw new ArgumentException("password must not be empty", nameof(plain));
        return BCrypt.Net.BCrypt.HashPassword(plain, WorkFactor);
    }

    public bool Verify(string plain, string hash)
    {
        if (string.IsNullOrEmpty(plain) || string.IsNullOrEmpty(hash)) return false;
        try { return BCrypt.Net.BCrypt.Verify(plain, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}
