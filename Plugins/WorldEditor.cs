using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class WorldEditor(GameWorld world, Session? session) : IWorldBuilder
{
    public IReadOnlyList<IWorldObject> Objects => [.. world.ToSnapshot().Objects.Select(obj => new PluginObject(obj))];

    public ITerrain Terrain { get; } = new TerrainEditor(world, session);

    public IWorldObject? Find(int id) => world.Find(id) is { } obj ? new PluginObject(obj) : null;

    public IWorldObject Add(string type, Vector3 position, IReadOnlyDictionary<string, object>? data = null, Quaternion? rotation = null, Vector3? scale = null, int? parentId = null)
    {
        if (!Enum.TryParse(type, ignoreCase: true, out WorldObjectType kind) || kind == WorldObjectType.Avatar)
            throw new ArgumentException($"cannot add world object of type {type}");

        Quaternion turn = rotation ?? Quaternion.Identity;
        Vector3 size = scale ?? Vector3.One;
        var obj = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = parentId ?? world.RootId,
            Type = kind,
            Position = [position.X, position.Y, position.Z],
            Rotation = [turn.X, turn.Y, turn.Z, turn.W],
            Scale = [size.X, size.Y, size.Z],
            Data = PackedData.FromPhoton(data?.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value) ?? []),
        };
        world.Add(obj);
        world.MarkChanged();

        if (session is not null)
        {
            GetNextGameBatch.SendAdded(session, 0, world.SubtreeSnapshot(obj.Id));
            session.Teams.Update();
        }
        return new PluginObject(obj);
    }

    public bool Remove(int id)
    {
        if (id == world.RootId || world.Find(id) is not { } obj || obj.Type == WorldObjectType.Avatar) return false;

        List<int> removedPrototypes = world.RemoveTree(id);
        world.MarkChanged();
        if (session is not null)
        {
            UnregisterWorldObject.Broadcast(session, id, removedPrototypes, null);
            session.Logic.Evaluate();
            session.Teams.Update();
        }
        return true;
    }

    public bool Move(int id, Vector3 position, Quaternion? rotation = null)
    {
        WorldObject? moved = null;
        bool found = world.Modify(id, obj =>
        {
            obj.Position = [position.X, position.Y, position.Z];
            if (rotation is Quaternion turn) obj.Rotation = [turn.X, turn.Y, turn.Z, turn.W];
            moved = obj;
        });
        if (!found || moved is null) return false;
        world.MarkChanged();

        Send(id, new EventData((byte)EventCode.UpdateWorldObject)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.ActorNr] = 0,
                [(byte)ParameterKey.PosX] = moved.Position[0],
                [(byte)ParameterKey.PosY] = moved.Position[1],
                [(byte)ParameterKey.PosZ] = moved.Position[2],
                [(byte)ParameterKey.RotX] = moved.Rotation[0],
                [(byte)ParameterKey.RotY] = moved.Rotation[1],
                [(byte)ParameterKey.RotZ] = moved.Rotation[2],
                [(byte)ParameterKey.RotW] = moved.Rotation[3],
            },
        });
        return true;
    }

    public bool SetData(int id, string key, object value) => SetData(id, new Dictionary<string, object> { [key] = value });

    public bool SetData(int id, IReadOnlyDictionary<string, object> values)
    {
        var change = values.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value);
        if (!world.Modify(id, obj => PackedData.Merge(obj.Data, change))) return false;
        world.MarkChanged();

        Send(id, new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.WorldObjectData] = change,
            },
        });
        return true;
    }

    void Send(int id, EventData evt)
    {
        if (session is null) return;
        Snapshot subtree = world.SubtreeSnapshot(id);
        foreach (Player player in session.Players)
            if (player.InWorld && player.Knows(subtree))
                player.Peer.Send(evt);
    }
}

sealed class TerrainEditor(GameWorld world, Session? session) : ITerrain
{
    const int BatchCubes = 2048;
    const int MaxFillCubes = 500_000;

    public int Count => Prototype()?.Cubes.Count ?? 0;

    public void Set(int x, int y, int z, byte material)
    {
        var changes = new List<byte>();
        CubeModel.AppendAdded(changes, Coordinate(x), Coordinate(y), Coordinate(z), material);
        Apply(changes);
    }

    public void Remove(int x, int y, int z)
    {
        var changes = new List<byte>();
        CubeModel.AppendDeleted(changes, Coordinate(x), Coordinate(y), Coordinate(z));
        Apply(changes);
    }

    public int Fill(int x1, int y1, int z1, int x2, int y2, int z2, byte material)
    {
        (int fromX, int toX) = Order(x1, x2);
        (int fromY, int toY) = Order(y1, y2);
        (int fromZ, int toZ) = Order(z1, z2);
        long total = (long)(toX - fromX + 1) * (toY - fromY + 1) * (toZ - fromZ + 1);
        if (total > MaxFillCubes) throw new ArgumentException($"fill of {total} cubes is larger than {MaxFillCubes}");

        var changes = new List<byte>();
        int pending = 0;
        for (int x = fromX; x <= toX; x++)
            for (int y = fromY; y <= toY; y++)
                for (int z = fromZ; z <= toZ; z++)
                {
                    CubeModel.AppendAdded(changes, Coordinate(x), Coordinate(y), Coordinate(z), material);
                    if (++pending < BatchCubes) continue;
                    Apply(changes);
                    changes.Clear();
                    pending = 0;
                }
        if (pending > 0) Apply(changes);
        return (int)total;
    }

    public void Clear()
    {
        if (Prototype() is not { } prototype) return;
        var changes = new List<byte>();
        int pending = 0;
        foreach ((short x, short y, short z) in prototype.Cubes.Positions())
        {
            CubeModel.AppendDeleted(changes, x, y, z);
            if (++pending < BatchCubes) continue;
            Apply(changes);
            changes.Clear();
            pending = 0;
        }
        if (pending > 0) Apply(changes);
    }

    void Apply(List<byte> changes)
    {
        Prototype prototype = Prototype() ?? throw new InvalidOperationException("this world has no terrain");
        byte[] data = [.. changes];
        prototype.Cubes.Apply(data);
        world.MarkChanged();
        if (session is null) return;

        var evt = new EventData((byte)EventCode.UpdatePrototype)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldInventoryID] = prototype.Id,
                [(byte)ParameterKey.WorldInventoryData] = data,
            },
        };
        foreach (Player player in session.Players)
            if (player.InWorld)
                player.Peer.Send(evt);
    }

    Prototype? Prototype() =>
        world.FindFirst(WorldObjectType.CubeModelPrototypeTerrain)?.PrototypeId is int id ? world.FindPrototype(id) : null;

    static (int From, int To) Order(int a, int b) => a <= b ? (a, b) : (b, a);

    static short Coordinate(int value) =>
        value is >= short.MinValue and <= short.MaxValue ? (short)value : throw new ArgumentOutOfRangeException(nameof(value), value, "cube coordinate out of range");
}
