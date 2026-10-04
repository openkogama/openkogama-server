using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Deadzone;

sealed record Explosion(float Trigger, float Fuse, float Radius, float Damage, float Push);

sealed record Monster(string Name, int Level, float Size, float Speed, float Health, float Damage, float Knockback, string? Skin = null, Explosion? Explosion = null);

abstract record NightStep
{
    public sealed record Spawn(Monster Monster) : NightStep;
    public sealed record Rush(Monster Monster, int Count) : NightStep;
    public sealed record Wait(float Seconds) : NightStep;
}

sealed record Night(string? Name, int MaxAlive, IReadOnlyList<NightStep> Steps);

sealed class NightPlan
{
    sealed class File
    {
        public float SpawnEvery { get; init; } = 2f;
        public int MaxAlive { get; init; } = 30;
        public float[][] SpawnPoints { get; init; } = [];
        public Dictionary<string, Monster> Monsters { get; init; } = [];
        public List<NightFile> Nights { get; init; } = [];
    }

    sealed class NightFile
    {
        public string? Name { get; init; }
        public int? MaxAlive { get; init; }
        public List<string> Steps { get; init; } = [];
    }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    readonly List<Night> _nights;
    readonly Vector3[] _spawnPoints;

    NightPlan(float spawnEvery, Vector3[] spawnPoints, IEnumerable<Monster> monsters, List<Night> nights)
    {
        SpawnEvery = spawnEvery;
        _spawnPoints = spawnPoints;
        _nights = nights;
        Skins = [.. monsters.Select(monster => monster.Skin).OfType<string>().Distinct()];
    }

    public float SpawnEvery { get; }
    public IReadOnlyList<string> Skins { get; }

    public Vector3 SpawnPoint(int index) => _spawnPoints[index % _spawnPoints.Length];

    public int Count => _nights.Count;

    public Night Night(int night) => _nights[Math.Clamp(night, 1, _nights.Count) - 1];

    public static NightPlan Parse(string json)
    {
        File file = JsonSerializer.Deserialize<File>(json, Options) ?? throw new InvalidDataException("nights.json is empty");
        if (file.Nights.Count == 0) throw new InvalidDataException("nights.json has no nights");
        if (file.SpawnPoints.Length == 0 || file.SpawnPoints.Any(point => point.Length != 3))
            throw new InvalidDataException("nights.json needs spawn points as [x, y, z]");

        List<Night> nights = [.. file.Nights.Select((night, index) => new Night(
            night.Name,
            night.MaxAlive ?? file.MaxAlive,
            [.. night.Steps.SelectMany(step => Steps(file, index + 1, step))]))];
        Vector3[] points = [.. file.SpawnPoints.Select(point => new Vector3(point[0], point[1], point[2]))];
        return new NightPlan(file.SpawnEvery, points, file.Monsters.Values, nights);
    }

    static IEnumerable<NightStep> Steps(File file, int night, string step)
    {
        string[] words = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words is ["wait", string seconds] && float.TryParse(seconds, CultureInfo.InvariantCulture, out float wait))
            return [new NightStep.Wait(wait)];
        bool rush = words is ["rush", ..];
        if (rush) words = words[1..];

        (int count, string name) = words switch
        {
            [string number, string monster] when int.TryParse(number, out int amount) => (amount, monster),
            [string monster] => (1, monster),
            _ => throw new InvalidDataException($"nights.json: night {night} has a step it cannot read: \"{step}\""),
        };
        if (!file.Monsters.TryGetValue(name, out Monster? found))
            throw new InvalidDataException($"nights.json: night {night} spawns an unknown monster \"{name}\"");
        return rush ? [new NightStep.Rush(found, count)] : Enumerable.Repeat<NightStep>(new NightStep.Spawn(found), count);
    }
}
