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

    public static Ticket? Verify(ECDsa key, string ticket, Func<string, bool> audience) => Verify(key, ticket, audience, out _);

    public static Ticket? Verify(ECDsa key, string ticket, Func<string, bool> audience, out string? problem)
    {
        problem = "unreadable ticket";
        int dot = ticket.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            byte[] data = Base64Url.DecodeFromChars(ticket.AsSpan(0, dot));
            if (!key.VerifyData(data, Base64Url.DecodeFromChars(ticket.AsSpan(dot + 1)), HashAlgorithmName.SHA256))
            {
                problem = "ticket not signed by the accounts server";
                return null;
            }
            Payload? payload = JsonSerializer.Deserialize<Payload>(data, Json);
            if (payload is null) return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            problem = payload.Exp + Skew.TotalSeconds < now ? $"ticket expired {now - payload.Exp} s ago"
                : payload.Iat - Skew.TotalSeconds > now ? $"ticket issued {payload.Iat - now} s in the future, check the clock"
                : !audience(payload.Aud) ? payload.Aud.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? $"ticket for {payload.Aud}, add it to addresses in server.json if that is this server"
                    : $"ticket for {payload.Aud}, not this server"
                : null;
            return problem is null ? new Ticket(payload.Jti, payload.Sub, payload.Name, payload.Aud) : null;
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
