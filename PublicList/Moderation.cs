using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.PublicList;

sealed class Moderation(string file, string? key)
{
    const string Endpoint = "https://api.openai.com/v1/moderations";
    const int MaxWaiting = 32;
    static readonly TimeSpan Pause = TimeSpan.FromMinutes(10);
    static readonly string[] ImageBlocks = ["sexual", "sexual/minors", "violence/graphic", "self-harm", "self-harm/intent", "self-harm/instructions"];
    static readonly string[] TitleBlocks = ["hate", "hate/threatening", "harassment", "harassment/threatening", "sexual", "sexual/minors",
        "violence/graphic", "self-harm", "self-harm/intent", "self-harm/instructions"];
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    readonly object _sync = new();
    readonly Dictionary<string, bool> _verdicts = Load(file);
    readonly HashSet<string> _asking = [];
    readonly SemaphoreSlim _one = new(1);
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    DateTime _pausedUntil;

    public bool On => key is not null;

    public static string Hash(byte[] image) => Convert.ToHexStringLower(SHA256.HashData(image));

    public static string TitleHash(string title) => Hash(Encoding.UTF8.GetBytes("title:" + title));

    public static string? Type(byte[] image) =>
        image.AsSpan().StartsWith(Jpeg) ? "image/jpeg"
        : image.AsSpan().StartsWith(Png) ? "image/png"
        : image.Length >= 12 && image.AsSpan(0, 4).SequenceEqual("RIFF"u8) && image.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    public bool? Verdict(string hash)
    {
        lock (_sync)
            return _verdicts.TryGetValue(hash, out bool allowed) ? allowed : null;
    }

    public void JudgeImage(string hash, byte[] image, string address) =>
        Judge(hash, $"thumbnail of {address}", new JsonObject
        {
            ["type"] = "image_url",
            ["image_url"] = new JsonObject { ["url"] = $"data:{Type(image)};base64,{Convert.ToBase64String(image)}" },
        }, ImageBlocks);

    public void JudgeTitle(string title, string address)
    {
        if (title.Length > 0)
            Judge(TitleHash(title), $"title \"{title}\" of {address}", new JsonObject { ["type"] = "text", ["text"] = title }, TitleBlocks);
    }

    void Judge(string hash, string what, JsonObject input, string[] blocks)
    {
        if (key is null)
            return;
        lock (_sync)
            if (_verdicts.ContainsKey(hash) || DateTime.UtcNow < _pausedUntil || _asking.Count >= MaxWaiting || !_asking.Add(hash))
                return;
        _ = Task.Run(async () =>
        {
            await _one.WaitAsync();
            try
            {
                if (await AskAsync(input, blocks) is bool allowed)
                {
                    lock (_sync)
                    {
                        _verdicts[hash] = allowed;
                        File.WriteAllText(file, JsonSerializer.Serialize(_verdicts));
                    }
                    Console.WriteLine(allowed ? $"{what} passed moderation" : $"{what} failed moderation and stays hidden");
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"{what} not checked: {error.Message}");
            }
            finally
            {
                lock (_sync)
                    _asking.Remove(hash);
                _one.Release();
            }
        });
    }

    async Task<bool?> AskAsync(JsonObject input, string[] blocks)
    {
        lock (_sync)
            if (DateTime.UtcNow < _pausedUntil)
                return null;
        var body = new JsonObject { ["model"] = "omni-moderation-latest", ["input"] = new JsonArray(input) };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using HttpResponseMessage response = await _http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != HttpStatusCode.BadRequest)
                lock (_sync)
                    _pausedUntil = DateTime.UtcNow + Pause;
            Console.WriteLine($"moderation answered {(int)response.StatusCode}: {text[..Math.Min(text.Length, 200)]}");
            return null;
        }
        if (JsonNode.Parse(text)?["results"]?[0]?["categories"] is not JsonObject categories)
            return null;
        return !blocks.Any(name => categories[name]?.GetValue<bool>() == true);
    }

    static Dictionary<string, bool> Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(file)) ?? [] : [];
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return [];
        }
    }
}
