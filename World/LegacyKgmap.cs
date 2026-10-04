using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;
using OpenKogama.Game;

namespace OpenKogama.World;

// Legacy kgmexporter maps (container versions 1 and 2) stored baked meshes instead of world batches:
//   prototypeCount, then per prototype: id, scale, vertexCount, vertices (x y z u v atlasU atlasV as floats + r g b a bytes)
//   instanceCount, then per instance: id, parentId, prototypeId, typeId (version 2 only), position, rotation, scale
// Every visible cube face is two triangles (a b c) (a c d), so cubes are rebuilt face by face.
public static class LegacyKgmap
{
    public const ushort LastVersion = 2;

    const int VertexSize = 32;
    const int VerticesPerFace = 6;
    const float Tolerance = 1e-3f;
    const string TemplatePath = "data/maps/default.kgmap";
    const int CubeModelItem = 10351;
    const int TerrainItem = 10349;
    const int AuthorId = 1;
    const float SpawnSearchRadius = 20f;
    const float SpawnClearance = 2f;
    const byte DefaultCorners = 1;
    const byte OneMaterial = 2;
    const byte FallbackMaterial = 24;

    static readonly byte[] IdentityCorners = [20, 120, 124, 24, 4, 104, 100, 0];
    static readonly int[] GlowingMaterials = [26, 28, 55];
    static readonly int[][] FaceSlots =
    [
        [0, 1, 2, 3],
        [4, 5, 6, 7],
        [7, 6, 1, 0],
        [5, 4, 3, 2],
        [4, 7, 0, 3],
        [6, 5, 2, 1],
    ];
    static readonly (int X, int Y, int Z)[] Neighbours = [(0, 1, 0), (0, -1, 0), (0, 0, -1), (0, 0, 1), (-1, 0, 0), (1, 0, 0)];
    static readonly int[] Opposite = [1, 0, 3, 2, 5, 4];
    static readonly int[] BorderAxis = [1, 1, 2, 2, 0, 0];
    static readonly int[][] TouchOrder = [[1, 0, 3, 2], [3, 2, 1, 0], [1, 0, 3, 2]];
    static readonly Dictionary<(byte Column, byte Row), byte> Materials = AtlasMaterials();
    static readonly HashSet<WorldObjectType> Kept =
    [
        WorldObjectType.CubeModel,
        WorldObjectType.CubeModelPrototypeTerrain,
        WorldObjectType.CubeModelTerrainFineGrained,
        WorldObjectType.Group,
    ];
    static readonly WorldObjectType[] SystemTypes =
    [
        WorldObjectType.SpawnPointBlue,
        WorldObjectType.GamePassProgressionDataObject,
        WorldObjectType.CubeModelTerrainFineGrained,
        WorldObjectType.GameBoosterDataObject,
    ];

    sealed record Instance(int Id, int ParentId, int PrototypeId, int TypeId, float[] Position, float[] Rotation, float[] Scale);

    sealed class CubeBuilder
    {
        public byte?[] Corners { get; } = new byte?[8];
        public byte?[] Materials { get; } = new byte?[6];
    }

    public static bool IsLegacy(ushort version) => version is >= 1 and <= LastVersion;

    public static (JsonObject Meta, List<byte[]> Batches) Read(BinaryReader reader, ushort version)
    {
        var prototypes = new List<Prototype>();
        var tops = new Dictionary<int, float>();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            int id = reader.ReadInt32();
            float scale = reader.ReadSingle();
            int vertices = reader.ReadInt32();
            byte[] mesh = reader.ReadBytes(vertices * VertexSize);
            if (mesh.Length < vertices * VertexSize) throw new EndOfStreamException("legacy .kgmap ends inside a mesh");
            (CubeModel cubes, float top) = Rebuild(mesh);
            prototypes.Add(new Prototype(id, scale == 0f ? 1f : scale, AuthorId, cubes));
            tops[id] = top * (scale == 0f ? 1f : scale);
        }

        var instances = new List<Instance>();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            int id = reader.ReadInt32();
            int parent = reader.ReadInt32();
            int prototype = reader.ReadInt32();
            int type = version >= 2 ? reader.ReadInt32() : (int)WorldObjectType.CubeModel;
            float[] position = [reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()];
            float[] rotation = [reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()];
            float[] scale = [reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()];
            instances.Add(new Instance(id, parent, prototype, type, position, rotation, scale));
        }

        float[] spawn = Spawn(instances, tops);
        Snapshot snapshot = Build(prototypes, instances, spawn);
        Console.WriteLine($"legacy .kgmap v{version}: {prototypes.Count} meshes rebuilt into {prototypes.Sum(p => p.Cubes.Count)} cubes, {instances.Count} objects");
        var meta = new JsonObject { ["Spawn"] = new JsonArray(spawn[0], spawn[1], spawn[2]) };
        return (meta, [WorldSerializer.Write(snapshot)]);
    }

    static (CubeModel Cubes, float Top) Rebuild(byte[] mesh)
    {
        var cubes = new Dictionary<(int X, int Y, int Z), CubeBuilder>();
        var ambiguous = new List<Face>();
        float top = float.MinValue;
        Span<Vector2> uvs = stackalloc Vector2[4];
        ReadOnlySpan<int> order = [0, 1, 2, 5];

        for (int index = 0; index + VerticesPerFace <= mesh.Length / VertexSize; index += VerticesPerFace)
        {
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                ReadOnlySpan<byte> vertex = mesh.AsSpan((index + order[i]) * VertexSize, VertexSize);
                corners[i] = new Vector3(Single(vertex, 0), Single(vertex, 4), Single(vertex, 8));
                uvs[i] = new Vector2(Single(vertex, 12), Single(vertex, 16));
                top = MathF.Max(top, corners[i].Y);
            }
            ReadOnlySpan<byte> first = mesh.AsSpan(index * VertexSize, VertexSize);
            byte material = Materials.GetValueOrDefault((first[29], first[30]), FallbackMaterial);

            int[] kinds = FaceKinds(corners, uvs);
            bool sure = kinds.Length == 1;
            var choices = new List<(int Kind, (int X, int Y, int Z) Grid)>();
            foreach (int kind in kinds)
            {
                (int X, int Y, int Z)[] options = Options(corners, FaceSlots[kind], out bool gridSure);
                sure &= gridSure;
                foreach ((int X, int Y, int Z) option in options)
                    choices.Add((kind, option));
            }
            var face = new Face(corners, material, [.. choices]);
            if (sure) Apply(cubes, face, face.Choices[0]);
            else ambiguous.Add(face);
        }

        foreach (Face face in ambiguous)
            Apply(cubes, face, face.Choices.MaxBy(choice => Evidence(cubes, face, choice)));
        InferHidden(cubes);

        return (Pack(cubes), top == float.MinValue ? 0f : top);
    }

    // The exporter only hid a face when the neighbour's touching face had the very same corners,
    // so a corner that no exported face shows is copied from the neighbour across the hidden face.
    static void InferHidden(Dictionary<(int X, int Y, int Z), CubeBuilder> cubes)
    {
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (((int x, int y, int z), CubeBuilder cube) in cubes)
            {
                for (int kind = 0; kind < FaceSlots.Length; kind++)
                {
                    int[] slots = FaceSlots[kind];
                    if (slots.All(slot => cube.Corners[slot] is not null)) continue;
                    (int dx, int dy, int dz) = Neighbours[kind];
                    var next = (x + dx, y + dy, z + dz);
                    if (!cubes.TryGetValue(next, out CubeBuilder? neighbour)) continue;

                    int[] across = FaceSlots[Opposite[kind]];
                    int[] order = TouchOrder[BorderAxis[kind]];
                    for (int i = 0; i < 4; i++)
                    {
                        if (cube.Corners[slots[i]] is not null || neighbour.Corners[across[order[i]]] is not byte shared) continue;
                        Vector3 position = new Vector3(next.Item1, next.Item2, next.Item3) + Decode(shared);
                        cube.Corners[slots[i]] = Encode(position - new Vector3(x, y, z));
                        changed = true;
                    }
                }
            }
        }
    }

    sealed record Face(Vector3[] Corners, byte Material, (int Kind, (int X, int Y, int Z) Grid)[] Choices);

    static void Apply(Dictionary<(int X, int Y, int Z), CubeBuilder> cubes, Face face, (int Kind, (int X, int Y, int Z) Grid) choice)
    {
        (int kind, (int x, int y, int z)) = choice;
        if (!cubes.TryGetValue(choice.Grid, out CubeBuilder? cube)) cubes[choice.Grid] = cube = new CubeBuilder();
        for (int i = 0; i < 4; i++)
            cube.Corners[FaceSlots[kind][i]] ??= Encode(face.Corners[i] - new Vector3(x, y, z));
        cube.Materials[kind] ??= face.Material;
    }

    static int Evidence(Dictionary<(int X, int Y, int Z), CubeBuilder> cubes, Face face, (int Kind, (int X, int Y, int Z) Grid) choice)
    {
        (int kind, (int x, int y, int z)) = choice;
        if (!cubes.TryGetValue(choice.Grid, out CubeBuilder? cube)) return 0;
        int score = 1;
        for (int i = 0; i < 4; i++)
        {
            if (cube.Corners[FaceSlots[kind][i]] is not byte known) continue;
            score += known == Encode(face.Corners[i] - new Vector3(x, y, z)) ? 2 : -4;
        }
        if (cube.Materials[kind] is byte material && material != face.Material) score -= 4;
        return score;
    }

    static float Single(ReadOnlySpan<byte> vertex, int offset) => BinaryPrimitives.ReadSingleLittleEndian(vertex[offset..]);

    static int[] FaceKinds(ReadOnlySpan<Vector3> corners, ReadOnlySpan<Vector2> uvs)
    {
        Span<float> errors = stackalloc float[FaceSlots.Length];
        for (int kind = 0; kind < FaceSlots.Length; kind++)
        {
            errors[kind] = 0f;
            for (int i = 0; i < 4; i++)
                errors[kind] += Vector2.Distance(FaceUv(corners[i], kind), uvs[i]);
        }
        float[] sorted = errors.ToArray();
        int[] matching = [.. Enumerable.Range(0, FaceSlots.Length).Where(kind => sorted[kind] <= Tolerance).OrderBy(kind => sorted[kind])];
        return matching.Length > 0 ? matching : [Enumerable.Range(0, FaceSlots.Length).MinBy(kind => sorted[kind])];
    }

    static Vector2 FaceUv(Vector3 local, int kind) => 0.5f * kind switch
    {
        0 => new Vector2(local.X + 0.5f, local.Z + 0.5f),
        1 => new Vector2(-local.X - 0.5f, local.Z + 0.5f),
        2 => new Vector2(local.X + 0.5f, local.Y + 0.5f),
        3 => new Vector2(-local.X - 0.5f, local.Y + 0.5f),
        4 => new Vector2(-local.Z - 0.5f, local.Y + 0.5f),
        _ => new Vector2(local.Z + 0.5f, local.Y + 0.5f),
    };

    static (int X, int Y, int Z)[] Options(Vector3[] corners, int[] slots, out bool sure)
    {
        sure = true;
        var axes = new int[3][];
        for (int axis = 0; axis < 3; axis++)
        {
            float min = float.MaxValue, max = float.MinValue, side = 0f;
            for (int i = 0; i < 4; i++)
            {
                float value = corners[i][axis];
                min = MathF.Min(min, value);
                max = MathF.Max(max, value);
                side += Identity(slots[i])[axis];
            }
            int low = (int)MathF.Ceiling(max - 0.5f - Tolerance);
            int high = (int)MathF.Floor(min + 0.5f + Tolerance);
            if (low == high) axes[axis] = [low];
            else if (low > high) axes[axis] = [(int)MathF.Round((min + max) / 2f)];
            else if (side != 0f) axes[axis] = side > 0f ? [low] : [high];
            else
            {
                axes[axis] = [low, high];
                sure = false;
            }
        }
        return [.. from x in axes[0] from y in axes[1] from z in axes[2] select (x, y, z)];
    }

    static Vector3 Identity(int slot) => Decode(IdentityCorners[slot]);

    static Vector3 Decode(byte corner) => new(-0.5f + corner / 25 * 0.25f, -0.5f + corner / 5 % 5 * 0.25f, -0.5f + corner % 5 * 0.25f);

    static byte Encode(Vector3 offset)
    {
        int Step(float value) => Math.Clamp((int)MathF.Round((value + 0.5f) / 0.25f), 0, 4);
        return (byte)(Step(offset.X) * 25 + Step(offset.Y) * 5 + Step(offset.Z));
    }

    static CubeModel Pack(Dictionary<(int X, int Y, int Z), CubeBuilder> cubes)
    {
        using var stream = new MemoryStream();
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, cubes.Count);
        stream.Write(buffer);

        foreach (((int x, int y, int z), CubeBuilder cube) in cubes)
        {
            foreach (int value in (ReadOnlySpan<int>)[x, y, z])
            {
                BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
                stream.Write(buffer[..2]);
            }

            byte[] corners = [.. Enumerable.Range(0, 8).Select(slot => cube.Corners[slot] ?? IdentityCorners[slot])];
            byte common = cube.Materials.Where(material => material is not null)
                .GroupBy(material => material!.Value)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .DefaultIfEmpty(FallbackMaterial)
                .First();
            byte[] materials = [.. cube.Materials.Select(material => material ?? common)];

            bool identity = corners.AsSpan().SequenceEqual(IdentityCorners);
            bool single = materials.All(material => material == materials[0]);
            stream.WriteByte((byte)((1 << 2) | (identity ? DefaultCorners : 0) | (single ? OneMaterial : 0)));
            if (!identity) stream.Write(corners);
            if (single) stream.WriteByte(materials[0]);
            else stream.Write(materials);
        }

        return CubeModel.FromBytes(stream.ToArray());
    }

    static float[] Spawn(List<Instance> instances, Dictionary<int, float> tops)
    {
        if (instances.Count == 0) return [0f, 10f, 0f];
        float x = Median(instances.Select(instance => instance.Position[0]));
        float z = Median(instances.Select(instance => instance.Position[2]));
        float y = instances
            .Where(instance => MathF.Abs(instance.Position[0] - x) <= SpawnSearchRadius && MathF.Abs(instance.Position[2] - z) <= SpawnSearchRadius)
            .Select(instance => instance.Position[1] + tops.GetValueOrDefault(instance.PrototypeId))
            .DefaultIfEmpty(instances.Max(instance => instance.Position[1] + tops.GetValueOrDefault(instance.PrototypeId)))
            .Max();
        return [x, y + SpawnClearance, z];
    }

    static float Median(IEnumerable<float> values)
    {
        float[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    static Snapshot Build(List<Prototype> prototypes, List<Instance> instances, float[] spawn)
    {
        Snapshot template = GameWorld.Load(Path.Combine(AppContext.BaseDirectory, TemplatePath)).ToSnapshot();
        WorldObject root = template.Objects.First(obj => obj.ParentId == -1);

        var groups = instances.Where(instance => instance.TypeId == (int)WorldObjectType.Group).Select(instance => instance.Id).ToHashSet();
        groups.Add(root.Id);
        var objects = new List<WorldObject> { root };
        foreach (Instance instance in instances.Where(instance => instance.Id != root.Id && Kept.Contains((WorldObjectType)instance.TypeId)))
        {
            if (!groups.Contains(instance.ParentId) || instance.ParentId == instance.Id) continue;
            var type = (WorldObjectType)instance.TypeId;
            objects.Add(new WorldObject
            {
                Id = instance.Id,
                ParentId = instance.ParentId,
                ItemId = type == WorldObjectType.CubeModelPrototypeTerrain ? TerrainItem : CubeModelItem,
                Type = type,
                Position = instance.Position,
                Rotation = instance.Rotation,
                Scale = instance.Scale,
                Data = type == WorldObjectType.Group ? [] : [("protoTypeID", PackedType.Int32, instance.PrototypeId)],
            });
        }

        var allPrototypes = new List<Prototype>(prototypes);
        int nextObject = objects.Max(obj => obj.Id) + 1;
        int nextPrototype = allPrototypes.Select(prototype => prototype.Id).DefaultIfEmpty(0).Max() + 1;
        foreach (WorldObject system in template.Objects.Where(obj => SystemTypes.Contains(obj.Type) && obj.ParentId == root.Id))
        {
            if (objects.Any(obj => obj.Type == system.Type)) continue;
            var copy = new WorldObject
            {
                Id = nextObject++,
                ParentId = root.Id,
                ItemId = system.ItemId,
                Type = system.Type,
                Position = system.Type == WorldObjectType.SpawnPointBlue ? spawn : system.Position,
                Rotation = system.Rotation,
                Scale = system.Scale,
                Data = system.Data,
            };
            if (system.PrototypeId is int prototypeId && template.Prototypes.FirstOrDefault(prototype => prototype.Id == prototypeId) is { } source)
            {
                allPrototypes.Add(new Prototype(nextPrototype, source.Scale, source.AuthorId, source.Cubes.Clone()));
                copy.Data = [.. system.Data.Where(pair => pair.Key != "protoTypeID"), ("protoTypeID", PackedType.Int32, nextPrototype)];
                nextPrototype++;
            }
            objects.Add(copy);
        }

        return new Snapshot(allPrototypes, objects, [], []);
    }

    static Dictionary<(byte Column, byte Row), byte> AtlasMaterials()
    {
        var materials = new Dictionary<(byte, byte), byte>();
        for (int material = 0; material < 63; material++)
        {
            int index = Array.IndexOf(GlowingMaterials, material) is int glowing and >= 0
                ? 63 - GlowingMaterials.Length + glowing
                : material - GlowingMaterials.Count(value => value < material);
            int column = index % 16;
            int rowFromTop = 16 - (index - column) / 16 - 1;
            materials.TryAdd(((byte)column, (byte)rowFromTop), (byte)material);
        }
        return materials;
    }
}
