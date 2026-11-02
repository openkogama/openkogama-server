using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.Accounts;

public sealed record ScanResult(int Malicious, int Suspicious, int Total);

public sealed class VirusScan
{
    const string Api = "https://www.virustotal.com/api/v3/";
    const int MaxPolls = 20;
    static readonly TimeSpan Gap = TimeSpan.FromSeconds(16);
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(45);
    static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);

    readonly PluginStore _store;
    readonly string _folder;
    readonly string? _key = Environment.GetEnvironmentVariable("OPENKOGAMA_VIRUSTOTAL_KEY") is { Length: > 0 } key ? key : null;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    readonly SemaphoreSlim _one = new(1);
    readonly HashSet<long> _queued = [];

    public VirusScan(PluginStore store, string folder)
    {
        _store = store;
        _folder = folder;
        if (_key is null) return;
        _http.DefaultRequestHeaders.Add("x-apikey", _key);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _ = Task.Run(async () =>
        {
            while (true)
            {
                foreach (PluginVersion version in _store.Unscanned()) Queue(version);
                await Task.Delay(Retry);
            }
        });
    }

    public bool On => _key is not null;

    public static string Link(PluginVersion version) => "https://www.virustotal.com/gui/file/" + Path.GetFileNameWithoutExtension(version.File);

    public void Queue(PluginVersion version)
    {
        if (!On) return;
        lock (_queued)
            if (!_queued.Add(version.Id)) return;
        _ = Task.Run(async () =>
        {
            await _one.WaitAsync();
            try
            {
                ScanResult? result = await ScanAsync(version);
                if (result is not null)
                {
                    _store.SetScan(version.Id, result);
                    Console.WriteLine($"plugins: {version.Name} {version.Version} scanned, {result.Malicious} of {result.Total} engines flagged it");
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                Console.WriteLine($"plugins: scan of {version.Name} {version.Version} failed: {error.Message}");
            }
            finally
            {
                await Task.Delay(Gap);
                lock (_queued) _queued.Remove(version.Id);
                _one.Release();
            }
        });
    }

    async Task<ScanResult?> ScanAsync(PluginVersion version)
    {
        string sha = Path.GetFileNameWithoutExtension(version.File);
        using HttpResponseMessage known = await _http.GetAsync(Api + "files/" + sha);
        if (known.IsSuccessStatusCode && Stats(JsonNode.Parse(await known.Content.ReadAsStringAsync())?["data"]?["attributes"]?["last_analysis_stats"]) is ScanResult cached)
            return cached;
        if (known.StatusCode != HttpStatusCode.NotFound && !known.IsSuccessStatusCode)
            throw new HttpRequestException($"VirusTotal answered {(int)known.StatusCode}");
        string path = Path.Combine(_folder, version.File);
        if (!File.Exists(path)) return null;
        await Task.Delay(Gap);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", version.Name);
        using HttpResponseMessage sent = await _http.PostAsync(Api + "files", form);
        if (!sent.IsSuccessStatusCode) throw new HttpRequestException($"VirusTotal upload answered {(int)sent.StatusCode}");
        string? analysis = JsonNode.Parse(await sent.Content.ReadAsStringAsync())?["data"]?["id"]?.GetValue<string>();
        if (analysis is null) return null;
        for (int attempt = 0; attempt < MaxPolls; attempt++)
        {
            await Task.Delay(Poll);
            using HttpResponseMessage check = await _http.GetAsync(Api + "analyses/" + analysis);
            if (!check.IsSuccessStatusCode) continue;
            JsonNode? attributes = JsonNode.Parse(await check.Content.ReadAsStringAsync())?["data"]?["attributes"];
            if (attributes?["status"]?.GetValue<string>() == "completed" && Stats(attributes["stats"]) is ScanResult result) return result;
        }
        return null;
    }

    static ScanResult? Stats(JsonNode? stats)
    {
        if (stats is not JsonObject counts) return null;
        int Count(string name) => counts[name]?.GetValue<int>() ?? 0;
        int malicious = Count("malicious"), suspicious = Count("suspicious");
        int total = malicious + suspicious + Count("undetected") + Count("harmless");
        return total > 0 ? new ScanResult(malicious, suspicious, total) : null;
    }
}
