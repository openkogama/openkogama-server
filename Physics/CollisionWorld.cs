using System.Numerics;
using OpenKogama.World;

namespace OpenKogama.Physics;

readonly record struct VoxelHit(float Distance, Vector3 Point, Vector3 Normal, Face Face, byte Material, int ObjectId);

sealed class CollisionWorld
{
    public const float DefaultGravity = 30f;

    const int ChunkSize = 32;
    const int FineGrainedChunkSize = 16;
    const float CubeHalfDiagonal = 0.8660254f;

    static readonly (int X, int Y, int Z, FaceFlags Side, FaceFlags Opposite)[] Neighbors =
    [
        (0, 1, 0, FaceFlags.Top, FaceFlags.Bottom),
        (0, -1, 0, FaceFlags.Bottom, FaceFlags.Top),
        (0, 0, 1, FaceFlags.Back, FaceFlags.Front),
        (0, 0, -1, FaceFlags.Front, FaceFlags.Back),
        (1, 0, 0, FaceFlags.Right, FaceFlags.Left),
        (-1, 0, 0, FaceFlags.Left, FaceFlags.Right),
    ];

    sealed record Body(
        int Index,
        int ObjectId,
        Game.CubeModel Cubes,
        Matrix4x4 LocalToWorld,
        Matrix4x4 WorldToLocal,
        Vector3 WorldMin,
        Vector3 WorldMax,
        (int X, int Y, int Z) CubeMin,
        (int X, int Y, int Z) CubeMax,
        int ChunkSize,
        float PrototypeScale,
        float LocalPerWorld);

    sealed class CubeData(byte[] cube, byte hidden, Vector3[] local, Vector3[] world)
    {
        public byte[] Cube => cube;
        public byte Hidden => hidden;
        public Vector3[] Local => local;
        public Vector3[] World => world;
    }

    readonly List<Body> _bodies;
    readonly Dictionary<(int Body, int X, int Y, int Z), CubeData?> _cubes = [];

    CollisionWorld(List<Body> bodies, float gravity, float? terrainBottom)
    {
        _bodies = bodies;
        Gravity = gravity;
        TerrainBottom = terrainBottom;
    }

    public float Gravity { get; }
    public float? TerrainBottom { get; }

    public static CollisionWorld Build(GameWorld world)
    {
        Snapshot snapshot = world.ToSnapshot();
        Dictionary<int, WorldObject> objects = snapshot.Objects.ToDictionary(obj => obj.Id);
        int? fineGrained = world.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId;
        var matrices = new Dictionary<int, Matrix4x4?>();

        Matrix4x4? WorldMatrix(WorldObject obj, int depth)
        {
            if (matrices.TryGetValue(obj.Id, out Matrix4x4? known)) return known;
            Matrix4x4? result = null;
            if (obj.Type is not (WorldObjectType.Avatar or WorldObjectType.BuildModeAvatar) && depth < 64)
            {
                Matrix4x4 local = Local(obj, world);
                if (!objects.TryGetValue(obj.ParentId, out WorldObject? parent)) result = local;
                else if (WorldMatrix(parent, depth + 1) is Matrix4x4 parentMatrix) result = local * parentMatrix;
            }
            matrices[obj.Id] = result;
            return result;
        }

        var bodies = new List<Body>();
        float? terrainBottom = null;
        foreach (WorldObject obj in snapshot.Objects)
        {
            if (obj.Type is not (WorldObjectType.CubeModel or WorldObjectType.CubeModelPrototypeTerrain or WorldObjectType.CubeModelTerrainFineGrained)) continue;
            if (obj.PrototypeId is not int prototypeId || world.FindPrototype(prototypeId) is not { } prototype) continue;
            if (WorldMatrix(obj, 0) is not Matrix4x4 matrix || !Matrix4x4.Invert(matrix, out Matrix4x4 inverse)) continue;
            if (prototype.Cubes.Bounds() is not var ((minX, minY, minZ), (maxX, maxY, maxZ))) continue;

            Vector3 worldMin = new(float.PositiveInfinity);
            Vector3 worldMax = new(float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new((corner & 1) == 0 ? minX - 0.5f : maxX + 0.5f, (corner & 2) == 0 ? minY - 0.5f : maxY + 0.5f, (corner & 4) == 0 ? minZ - 0.5f : maxZ + 0.5f);
                Vector3 point = Vector3.Transform(local, matrix);
                worldMin = Vector3.Min(worldMin, point);
                worldMax = Vector3.Max(worldMax, point);
            }

            float smallest = MathF.Min(new Vector3(matrix.M11, matrix.M12, matrix.M13).Length(),
                MathF.Min(new Vector3(matrix.M21, matrix.M22, matrix.M23).Length(), new Vector3(matrix.M31, matrix.M32, matrix.M33).Length()));
            if (smallest <= 0f) continue;

            if (obj.Type == WorldObjectType.CubeModelPrototypeTerrain) terrainBottom = worldMin.Y;
            bodies.Add(new Body(bodies.Count, obj.Id, prototype.Cubes, matrix, inverse, worldMin, worldMax, (minX, minY, minZ), (maxX, maxY, maxZ),
                prototypeId == fineGrained ? FineGrainedChunkSize : ChunkSize, prototype.Scale, 1f / smallest));
        }

        float gravity = DefaultGravity;
        if (world.FindFirst(WorldObjectType.GravityCube) is { } cube && cube.Data.Find(pair => pair.Key == "gravity").Value is { } value)
            gravity = Convert.ToSingle(value);
        return new CollisionWorld(bodies, gravity, terrainBottom);
    }

    static Matrix4x4 Local(WorldObject obj, GameWorld world)
    {
        Vector3 scale = obj.Type is WorldObjectType.CubeModel or WorldObjectType.CubeModelPrototypeTerrain or WorldObjectType.CubeModelTerrainFineGrained
            && obj.PrototypeId is int id && world.FindPrototype(id) is { } prototype
            ? new Vector3(prototype.Scale)
            : new Vector3(obj.Scale[0], obj.Scale[1], obj.Scale[2]);
        var rotation = new Quaternion(obj.Rotation[0], obj.Rotation[1], obj.Rotation[2], obj.Rotation[3]);
        rotation = rotation.LengthSquared() > 0f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(obj.Position[0], obj.Position[1], obj.Position[2]);
    }

    public VoxelHit? Cast(Vector3 origin, Vector3 direction, Vector3 radius, float distance)
    {
        if (direction == Vector3.Zero || radius.X == 0f || radius.Y == 0f || radius.Z == 0f) return null;

        float maxRadius = MathF.Max(radius.X, MathF.Max(radius.Y, radius.Z));
        Vector3 end = origin + direction * distance;
        Vector3 sweepMin = Vector3.Min(origin, end) - new Vector3(maxRadius);
        Vector3 sweepMax = Vector3.Max(origin, end) + new Vector3(maxRadius);

        Vector3 eOrigin = origin / radius;
        Vector3 eDirection = UnityMath.Normalized(direction / radius);
        float eDistance = (direction * distance / radius).Length();

        float best = float.PositiveInfinity;
        VoxelHit? hit = null;
        Span<Vector3> corners = stackalloc Vector3[8];
        Span<Vector3> face = stackalloc Vector3[4];

        foreach (Body body in _bodies)
        {
            if (!Overlaps(body, sweepMin, sweepMax)) continue;

            Vector3 a = Vector3.Transform(origin, body.WorldToLocal);
            Vector3 b = Vector3.Transform(end, body.WorldToLocal);
            float pad = maxRadius * body.LocalPerWorld;
            Vector3 low = Vector3.Min(a, b) - new Vector3(pad);
            Vector3 high = Vector3.Max(a, b) + new Vector3(pad);

            int x0 = Math.Max(body.CubeMin.X, (int)MathF.Ceiling(low.X - 0.5f)), x1 = Math.Min(body.CubeMax.X, (int)MathF.Floor(high.X + 0.5f));
            int y0 = Math.Max(body.CubeMin.Y, (int)MathF.Ceiling(low.Y - 0.5f)), y1 = Math.Min(body.CubeMax.Y, (int)MathF.Floor(high.Y + 0.5f));
            int z0 = Math.Max(body.CubeMin.Z, (int)MathF.Ceiling(low.Z - 0.5f)), z1 = Math.Min(body.CubeMax.Z, (int)MathF.Floor(high.Z + 0.5f));

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (Cube(body, x, y, z) is not { } data || data.Hidden == 63) continue;
                        byte[] cube = data.Cube;
                        byte hidden = data.Hidden;
                        for (int i = 0; i < 8; i++) corners[i] = data.World[i] / radius;

                        foreach (FaceFlags flag in CubeShape.AllFaces)
                        {
                            if ((hidden & (byte)flag) != 0) continue;
                            Face side = CubeShape.ToFace(flag);
                            CubeShape.FaceVertices(corners, side, face);
                            Triangle(face[0], face[3], face[2]);
                            Triangle(face[2], face[1], face[0]);

                            void Triangle(Vector3 p1, Vector3 p2, Vector3 p3)
                            {
                                if (!TriangleCheck.Sweep(p1, p2, p3, eOrigin, eDirection, eDistance, out Vector3 point, out float found)) return;
                                if (found >= best || (eOrigin - point).LengthSquared() < 1f) return;
                                best = found;
                                hit = new VoxelHit(0f, point * radius, UnityMath.TriangleNormal(p1 * radius, p2 * radius, p3 * radius), side,
                                    CubeShape.Material(cube, side), body.ObjectId);
                            }
                        }
                    }
        }

        return hit is VoxelHit result ? result with { Distance = (eDirection * best * radius).Length() } : null;
    }

    public bool Overlap(Vector3 position, Vector3 radius)
    {
        float maxRadius = MathF.Max(radius.X, MathF.Max(radius.Y, radius.Z));
        Vector3 low = position - new Vector3(maxRadius);
        Vector3 high = position + new Vector3(maxRadius);
        foreach (Body body in _bodies)
            if (Overlaps(body, low, high) && Overlap(body, position, radius)) return true;
        return false;
    }

    bool Overlap(Body body, Vector3 position, Vector3 radius)
    {
        Vector3 halfDiagonal = new(CubeHalfDiagonal * body.PrototypeScale);
        Vector3 extended = radius + halfDiagonal;
        Vector3 reduced = radius - halfDiagonal;
        bool reducedExists = reduced.X > 0f && reduced.Y > 0f && reduced.Z > 0f;
        Vector3 localPosition = Vector3.Transform(position, body.WorldToLocal);

        Vector3 right = Vector3.TransformNormal(Vector3.UnitX, body.LocalToWorld);
        Vector3 up = Vector3.TransformNormal(Vector3.UnitY, body.LocalToWorld);
        Vector3 forward = Vector3.TransformNormal(Vector3.UnitZ, body.LocalToWorld);
        Vector3 TangentNormal(Vector3 first, Vector3 second) =>
            UnityMath.Normalized(Vector3.Cross(UnityMath.Normalized(first / radius), UnityMath.Normalized(second / radius))) * radius;

        Vector3 extents = Vector3.Zero;
        foreach (Vector3 normal in (ReadOnlySpan<Vector3>)[TangentNormal(right, forward), TangentNormal(up, forward), TangentNormal(up, right)])
            extents = Vector3.Max(extents, Vector3.Abs(Vector3.TransformNormal(normal, body.WorldToLocal)));

        Vector3 low = localPosition - extents;
        Vector3 high = localPosition + extents;
        int x0 = Math.Max(body.CubeMin.X, (int)MathF.Floor(low.X + 0.5f)), x1 = Math.Min(body.CubeMax.X, (int)MathF.Floor(high.X + 0.5f));
        int y0 = Math.Max(body.CubeMin.Y, (int)MathF.Floor(low.Y + 0.5f)), y1 = Math.Min(body.CubeMax.Y, (int)MathF.Floor(high.Y + 0.5f));
        int z0 = Math.Max(body.CubeMin.Z, (int)MathF.Floor(low.Z + 0.5f)), z1 = Math.Min(body.CubeMax.Z, (int)MathF.Floor(high.Z + 0.5f));

        Span<Vector3> face = stackalloc Vector3[4];
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    if (Cube(body, x, y, z) is not { } data) continue;
                    Vector3 offset = Vector3.Transform(new Vector3(x, y, z), body.LocalToWorld) - position;
                    if ((offset / extended).LengthSquared() > 1f) continue;
                    if (reducedExists ? (offset / reduced).LengthSquared() <= 1f : CenterInside(data, localPosition, face)) return true;
                    if (Detailed(data, position, radius, face)) return true;
                }
        return false;
    }

    static bool CenterInside(CubeData data, Vector3 localPosition, Span<Vector3> face)
    {
        Vector3 rayEnd = localPosition + new Vector3(4f, 0f, 0f);
        int crossings = 0;
        foreach (FaceFlags flag in CubeShape.AllFaces)
        {
            CubeShape.FaceVertices(data.Local, CubeShape.ToFace(flag), face);
            if (UnityMath.LineFacet(localPosition, rayEnd, face[0], face[3], face[2])) crossings++;
            if (UnityMath.LineFacet(localPosition, rayEnd, face[2], face[1], face[0])) crossings++;
        }
        return crossings % 2 != 0;
    }

    static bool Detailed(CubeData data, Vector3 position, Vector3 radius, Span<Vector3> face)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = (data.World[i] - position) / radius;
            if (corners[i].LengthSquared() <= 1f) return true;
        }
        foreach (FaceFlags flag in CubeShape.AllFaces)
        {
            CubeShape.FaceVertices(corners, CubeShape.ToFace(flag), face);
            if (!TriangleCheck.Separated(face[0], face[3], face[2], 1f)) return true;
            if (!TriangleCheck.Separated(face[2], face[1], face[0], 1f)) return true;
        }
        return false;
    }

    CubeData? Cube(Body body, int x, int y, int z)
    {
        if (_cubes.TryGetValue((body.Index, x, y, z), out CubeData? known)) return known;
        CubeData? data = null;
        if (body.Cubes.Get(x, y, z) is { } cube)
        {
            var local = new Vector3[8];
            var world = new Vector3[8];
            CubeShape.Corners(cube, local);
            var center = new Vector3(x, y, z);
            for (int i = 0; i < 8; i++)
            {
                local[i] += center;
                world[i] = Vector3.Transform(local[i], body.LocalToWorld);
            }
            data = new CubeData(cube, HiddenSides(body, x, y, z, cube), local, world);
        }
        _cubes[(body.Index, x, y, z)] = data;
        return data;
    }

    static byte HiddenSides(Body body, int x, int y, int z, byte[] cube)
    {
        byte hidden = 0;
        var chunk = (Chunk(x, body.ChunkSize), Chunk(y, body.ChunkSize), Chunk(z, body.ChunkSize));
        foreach ((int dx, int dy, int dz, FaceFlags side, FaceFlags opposite) in Neighbors)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if ((Chunk(nx, body.ChunkSize), Chunk(ny, body.ChunkSize), Chunk(nz, body.ChunkSize)) != chunk) continue;
            if (body.Cubes.Get(nx, ny, nz) is { } neighbor && CubeShape.Hides(cube, neighbor, side, opposite)) hidden |= (byte)side;
        }
        return hidden;
    }

    static int Chunk(int value, int size) => (int)MathF.Floor((value + (float)(size / 2)) / size);

    static bool Overlaps(Body body, Vector3 low, Vector3 high) =>
        body.WorldMin.X <= high.X && body.WorldMax.X >= low.X
        && body.WorldMin.Y <= high.Y && body.WorldMax.Y >= low.Y
        && body.WorldMin.Z <= high.Z && body.WorldMax.Z >= low.Z;
}
