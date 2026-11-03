using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.Accounts;

public sealed record ScanResult(int Malicious, int Suspicious, int Total, string[] Engines)
{
    public int Flagged => Malicious + Suspicious;
    public bool Weak => Flagged is > 0 and <= 2 && !Engines.Any(VirusScan.Major);
}

public sealed class VirusScan
{
    const string Api = "https://www.virustotal.com/api/v3/";
    const int MaxPolls = 20;
    static readonly HashSet<string> MajorEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Kaspersky", "ESET-NOD32", "BitDefender", "Avast", "AVG", "Sophos", "McAfee", "Trellix", "Symantec", "TrendMicro",
        "TrendMicro-HouseCall", "DrWeb", "F-Secure", "GData", "Avira", "Malwarebytes", "Fortinet", "Emsisoft", "ZoneAlarm", "Panda", "Ikarus",
    };
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

    public static bool Major(string engine) => MajorEngines.Contains(engine);

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
                    string who = result.Engines.Length > 0 ? " (" + string.Join(", ", result.Engines) + ")" : "";
                    Console.WriteLine($"plugins: {version.Name} {version.Version} scanned, {result.Flagged} of {result.Total} engines flagged it{who}");
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
        if (known.IsSuccessStatusCode && JsonNode.Parse(await known.Content.ReadAsStringAsync())?["data"]?["attributes"] is JsonNode found
            && Stats(found["last_analysis_stats"], found["last_analysis_results"]) is ScanResult cached)
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
            if (attributes?["status"]?.GetValue<string>() == "completed" && Stats(attributes["stats"], attributes["results"]) is ScanResult result) return result;
        }
        return null;
    }

    static ScanResult? Stats(JsonNode? stats, JsonNode? results)
    {
        if (stats is not JsonObject counts) return null;
        int Count(string name) => counts[name]?.GetValue<int>() ?? 0;
        int malicious = Count("malicious"), suspicious = Count("suspicious");
        int total = malicious + suspicious + Count("undetected") + Count("harmless");
        if (total == 0) return null;
        var engines = new List<string>();
        if (results is JsonObject verdicts)
            foreach ((string engine, JsonNode? verdict) in verdicts)
                if (verdict?["category"]?.GetValue<string>() is "malicious" or "suspicious")
                    engines.Add(verdict["engine_name"]?.GetValue<string>() ?? engine);
        return new ScanResult(malicious, suspicious, total, [.. engines]);
    }
}
