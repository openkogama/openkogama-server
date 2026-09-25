using System.Text.Json;

namespace OpenKogama.Game;

public sealed class WorldTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string SourceGame { get; set; } = "";

    public string Path => System.IO.Path.Combine(Templates.Folder, File);
}

public static class Templates
{
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "data", "templates");

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static List<WorldTemplate>? _all;

    public static List<WorldTemplate> All => _all ??=
        JsonSerializer.Deserialize<List<WorldTemplate>>(File.ReadAllText(Path.Combine(Folder, "templates.json")), Options) ?? [];

    public static WorldTemplate? Find(string? id) => All.Find(template => template.Id == id);
}
