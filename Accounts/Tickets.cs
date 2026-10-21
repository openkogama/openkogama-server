using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenKogama.Accounts;

public sealed record Ticket(string Id, long Account, string Name, string Audience);

public static class Tickets
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    const int IdSize = 16;

    sealed record Payload(string Jti, long Sub, string Name, string Aud, long Iat, long Exp);

    public static string Issue(ECDsa key, long account, string name, string audience)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Payload(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(IdSize)), account, name, audience, now, now + (long)Lifetime.TotalSeconds);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        return Base64Url.EncodeToString(data) + "." + Base64Url.EncodeToString(key.SignData(data, HashAlgorithmName.SHA256));
    }

    public static Ticket? Verify(ECDsa key, string ticket, Func<string, bool> audience)
    {
        int dot = ticket.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            byte[] data = Base64Url.DecodeFromChars(ticket.AsSpan(0, dot));
            if (!key.VerifyData(data, Base64Url.DecodeFromChars(ticket.AsSpan(dot + 1)), HashAlgorithmName.SHA256)) return null;
            Payload? payload = JsonSerializer.Deserialize<Payload>(data, Json);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload is null || payload.Exp + Skew.TotalSeconds < now || payload.Iat - Skew.TotalSeconds > now || !audience(payload.Aud)) return null;
            return new Ticket(payload.Jti, payload.Sub, payload.Name, payload.Aud);
        }
        catch (Exception error) when (error is FormatException or JsonException or CryptographicException)
        {
            return null;
        }
    }

    public static ECDsa LoadOrCreate(string path)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(path))
        {
            key.ImportFromPem(File.ReadAllText(path));
            return key;
        }
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return key;
    }

    public static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static ECDsa FromPublicKey(string publicKey)
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        return key;
    }
}
