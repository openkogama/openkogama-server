using System.Security.Cryptography;
using System.Text;

namespace OpenKogama.Kogama;

// Mirrors MV.WorldObject.Security.SecurityHelper in the client. The Format field in the
// Join response is AES encrypted with a key baked into the client, so we derive the same
// one or the client throws while parsing the response.
public static class SecurityHelper
{
    const string Password = "P63oUa9unCY";

    static readonly Dictionary<string, string> Precomputed = new()
    {
        ["openkogama"] = "TLA5sckQhV5CpR11uZAZis0SDa7ER7NtnLlZhRQjpJ0=",
    };

    static readonly Lazy<(byte[] Key, byte[] Iv)> Secret = new(() =>
    {
        // The client takes 32 + 16 bytes off one PBKDF2 stream, so the IV is bytes 32..48.
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(Password),
            Encoding.ASCII.GetBytes(Password),
            iterations: 1000,
            HashAlgorithmName.SHA1,
            outputLength: 48);

        return (derived[..32], derived[32..48]);
    });

    public static string Encrypt(string text)
    {
        if (Precomputed.TryGetValue(text, out string? known)) return known;

        (byte[] key, byte[] iv) = Secret.Value;
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        byte[] plain = Encoding.Unicode.GetBytes(text);
        return Convert.ToBase64String(aes.EncryptCbc(plain, iv));
    }
}
