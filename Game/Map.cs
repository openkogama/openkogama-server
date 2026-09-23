using System.Text.Json;

namespace OpenKogama.Game;

public sealed class Map
{
    public sealed class TerrainData
    {
        public float Scale { get; set; } = 1f;
        public string Cubes { get; set; } = "";

        public byte[] CubeData => Convert.FromBase64String(Cubes);
    }

    public string Name { get; set; } = "";
    public TerrainData Terrain { get; set; } = new();
    public float[] Spawn { get; set; } = [0f, 2f, 0f];

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static Map? _default;

    public static Map Default
    {
        get
        {
            if (_default is not null) return _default;
            string path = Path.Combine(AppContext.BaseDirectory, "data", "maps", "default.json");
            _default = JsonSerializer.Deserialize<Map>(File.ReadAllText(path), Options) ?? new();
            return _default;
        }
    }
}
