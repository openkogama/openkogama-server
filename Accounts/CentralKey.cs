using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.Accounts;

public static class CentralKey
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly TimeSpan Refresh = TimeSpan.FromHours(1);
    static volatile ECDsa? _current;

    public static ECDsa? Current
    {
        get => _current;
        set => _current = value;
    }

    public static void Start(string url, string cache)
    {
        if (File.Exists(cache) && Load(File.ReadAllText(cache)) is ECDsa cached) Current = cached;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    string? key = JsonNode.Parse(await Http.GetStringAsync(url.TrimEnd('/') + "/key"))?["key"]?.GetValue<string>();
                    if (key is not null && Load(key) is ECDsa fetched)
                    {
                        Current = fetched;
                        File.WriteAllText(cache, key);
                    }
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or IOException)
                {
                    Console.WriteLine($"accounts: no key from {url}: {error.Message}");
                }
                await Task.Delay(Refresh);
            }
        });
    }

    static ECDsa? Load(string key)
    {
        try
        {
            return Tickets.FromPublicKey(key.Trim());
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
