using System.Security.Cryptography;

namespace OpenKogama.Accounts;

public static class Passwords
{
    const string Scheme = "pbkdf2-sha256";
    const int Iterations = 600_000;
    const int SaltSize = 16;
    const int HashSize = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (stored.Split('$') is not [Scheme, string rounds, string salt, string hash] || !int.TryParse(rounds, out int iterations)) return false;
        byte[] expected = Convert.FromBase64String(hash);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
