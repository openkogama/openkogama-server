using System.Collections.Concurrent;
using OpenKogama.Api;

namespace Worldgen;

readonly record struct ChunkCube(int X, int Y, int Z, Cube Cube);

sealed record Chunk(ChunkCube[] Cubes);

sealed class Chunks : IDisposable
{
    public const int Size = 32;

    readonly Hills _hills;
    readonly Action<string> _log;
    readonly TerrainGenerator _generator = new();
    readonly Dictionary<ulong, Cube> _shapes = [];
    readonly ConcurrentDictionary<(int X, int Z), Chunk?> _chunks = new();
    readonly BlockingCollection<(int X, int Z)> _requests = [];
    readonly CancellationTokenSource _stop = new();

    public Chunks(Hills hills, Action<string> log)
    {
        _hills = hills;
        _log = log;
        new Thread(Work) { IsBackground = true, Name = "worldgen", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public static (int X, int Z) Of((int X, int Y, int Z) cube) =>
        (KogamaTerrain.FloorDiv(cube.X + Size / 2, Size), KogamaTerrain.FloorDiv(cube.Z + Size / 2, Size));

    public static int Distance((int X, int Z) key, int x, int z)
    {
        int x0 = key.X * Size - Size / 2, z0 = key.Z * Size - Size / 2;
        int dx = Math.Max(Math.Max(x0 - x, x - (x0 + Size - 1)), 0);
        int dz = Math.Max(Math.Max(z0 - z, z - (z0 + Size - 1)), 0);
        return dx * dx + dz * dz;
    }

    public Chunk? Get((int X, int Z) key)
    {
        if (_chunks.TryGetValue(key, out Chunk? chunk))
            return chunk;
        if (_chunks.TryAdd(key, null))
            _requests.Add(key);
        return null;
    }

    public Chunk? Cached((int X, int Z) key) => _chunks.GetValueOrDefault(key);

    public void Keep(HashSet<(int X, int Z)> wanted)
    {
        foreach ((int X, int Z) key in _chunks.Keys)
            if (!wanted.Contains(key))
                _chunks.TryRemove(key, out _);
    }

    public void Dispose() => _stop.Cancel();

    void Work()
    {
        try
        {
            foreach ((int X, int Z) key in _requests.GetConsumingEnumerable(_stop.Token))
            {
                if (!_chunks.ContainsKey(key))
                    continue;
                try
                {
                    _chunks[key] = Generate(key.X, key.Z);
                }
                catch (Exception error)
                {
                    _log($"worldgen: chunk {key.X},{key.Z} failed: {error}");
                    _chunks[key] = new Chunk([]);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    Chunk Generate(int cx, int cz)
    {
        int x0 = cx * Size - Size / 2, z0 = cz * Size - Size / 2;
        var heights = new double[(Size + 1) * (Size + 1)];
        for (int k = 0; k <= Size; k++)
            for (int i = 0; i <= Size; i++)
                heights[k * (Size + 1) + i] = _hills.Height(x0 + i, z0 + k);
        int[,] H = KogamaTerrain.HeightsFromZ(heights, Size, Size);
        var (cubes, _, _) = KogamaTerrain.ExportCubes(H, (byte)Material.Green, _generator, pinEdges: true);
        return new Chunk([.. cubes.Select(cube => new ChunkCube(x0 + cube.X, cube.Y, z0 + cube.Z, Shape(cube.Corners)))]);
    }

    Cube Shape(byte[] corners)
    {
        ulong key = BitConverter.ToUInt64(corners);
        if (!_shapes.TryGetValue(key, out Cube? cube))
            _shapes[key] = cube = new Cube([Material.Green, Material.Green, Material.Green, Material.Green, Material.Green, Material.Green], corners);
        return cube;
    }
}
