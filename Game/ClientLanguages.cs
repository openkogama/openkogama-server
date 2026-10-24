using System.Collections.Concurrent;
using System.Text.Json;

namespace OpenKogama.Game;

public static class ClientLanguages
{
    const string Default = "en_US";
    const string English = "en";
    const string Folder = "Languages/";
    const string Extension = ".txt";
    const int MaxTag = 35;
    static readonly ConcurrentDictionary<string, SortedSet<string>> Files = new();

    public static string? Clean(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > MaxTag || !tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        string[] parts = tag.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0].Length is < 2 or > 3) return null;
        return string.Join('-', parts.Select((part, index) => index == 0 ? part.ToLowerInvariant() : part.Length == 2 ? part.ToUpperInvariant() : part));
    }

    public static string For(string language, string? client)
    {
        string[] parts = language.Split('-');
        if (parts[0] == English) return Default;
        SortedSet<string> files = Files.GetOrAdd((client is null ? null : BundleSets.For(client)) ?? BundleSets.Legacy, Load);
        string? region = parts.Skip(1).FirstOrDefault(part => part.Length == 2);
        if (region is not null && files.Contains($"{parts[0]}_{region}")) return $"{parts[0]}_{region}";
        if (files.Contains(parts[0])) return parts[0];
        return files.FirstOrDefault(file => file.StartsWith(parts[0] + "_", StringComparison.Ordinal)) ?? Default;
    }

    static SortedSet<string> Load(string set)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        string manifest = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "manifests", set + ".json");
        if (!File.Exists(manifest)) return files;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
        if (!document.RootElement.TryGetProperty("files", out JsonElement listed)) return files;
        foreach (JsonProperty file in listed.EnumerateObject())
            if (file.Name.StartsWith(Folder, StringComparison.Ordinal) && file.Name.EndsWith(Extension, StringComparison.Ordinal)
                && file.Name[Folder.Length..^Extension.Length] is { Length: > 0 } name && !name.Contains('/'))
                files.Add(name);
        return files;
    }
}
