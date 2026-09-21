using System.Security.Cryptography;
using System.Text;

namespace OpenKogama.Kogama;

// Mirrors MV.WorldObject.Security.SecurityHelper in the client. The Format field in the
// Join response is AES encrypted with a key baked into the client, so we derive the same
// one or the client throws while parsing the response.
public static class SecurityHelper
{
    const string Password = "P63oUa9unCY";

    static readonly byte[] Key;
    static readonly byte[] Iv;

    static SecurityHelper()
    {
        // The client takes 32 + 16 bytes off one PBKDF2 stream, so the IV is bytes 32..48.
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(Password),
            Encoding.ASCII.GetBytes(Password),
            iterations: 1000,
            HashAlgorithmName.SHA1,
            outputLength: 48);

        Key = derived[..32];
        Iv = derived[32..48];
    }

    public static string Encrypt(string text)
    {
        using Aes aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        byte[] plain = Encoding.Unicode.GetBytes(text);
        return Convert.ToBase64String(aes.EncryptCbc(plain, Iv));
    }
}
