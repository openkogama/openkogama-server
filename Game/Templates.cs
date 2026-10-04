using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class WorldTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string SourceGame { get; set; } = "";

    [JsonIgnore]
    public Func<GameWorld>? Build { get; set; }

    public string Path => System.IO.Path.Combine(Templates.Folder, File);
}

public static class Templates
{
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "data", "templates");
    public static readonly string DefaultPath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "default.kgmap");

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static List<WorldTemplate>? _all;

    public static List<WorldTemplate> All => _all ??=
        JsonSerializer.Deserialize<List<WorldTemplate>>(File.ReadAllText(Path.Combine(Folder, "templates.json")), Options) ?? [];

    public static WorldTemplate? Find(string? id) => All.Find(template => template.Id == id);

    public static void Register(WorldTemplate template)
    {
        lock (All)
        {
            All.RemoveAll(existing => existing.Id == template.Id);
            All.Add(template);
        }
    }

    public static GameWorld Create(string? id) =>
        Find(id) is { } template ? template.Build?.Invoke() ?? WorldConverter.Load(template.Path) : WorldConverter.Load(DefaultPath);
}
