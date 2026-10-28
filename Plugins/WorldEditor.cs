using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Physics;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class WorldEditor(GameWorld world, Session? session) : IWorldBuilder
{
    public IReadOnlyList<IWorldObject> Objects => [.. world.ToSnapshot().Objects.Select(obj => new PluginObject(obj))];

    public IReadOnlyList<WorldLink> Links => [.. world.LinksOf(objectLink: false).Select(link => new WorldLink(link.Id, link.From, link.To, false))];

    public IReadOnlyList<WorldLink> ObjectLinks => [.. world.LinksOf(objectLink: true).Select(link => new WorldLink(link.Id, link.From, link.To, true))];

    public ITerrain Terrain { get; } = new CubesEditor(world, session, null);

    public ICubeModel? Model(int objectId) => world.Find(objectId)?.PrototypeId is not null ? new CubesEditor(world, session, objectId) : null;

    public IWorldObject? Find(int id) => world.Find(id) is { } obj ? new PluginObject(obj) : null;

    public IReadOnlyList<IWorldObject> Markers(string name) => [.. WorldMarkers.Named(world, name).Select(obj => new PluginObject(obj))];

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

    public IWorldObject? AddItem(string name, Vector3 position, Quaternion? rotation = null, int? parentId = null)
    {
        string version = session?.Players.FirstOrDefault()?.ClientVersion ?? Kogama.Protocols.ClientProtocols.ServerVersion;
        if (Items.ForClient(version).Items.Find(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } item) return null;
        Quaternion turn = rotation ?? Quaternion.Identity;
        return AddItemToWorld.Place(world, session, item, [position.X, position.Y, position.Z], [turn.X, turn.Y, turn.Z, turn.W], null, 0, parentId ?? world.RootId, 0) is { } placed
            ? new PluginObject(placed)
            : null;
    }

    public bool Remove(int id)
    {
        if (id == world.RootId || world.Find(id) is not { } obj || obj.Type == WorldObjectType.Avatar) return false;

        List<(int Id, bool ObjectLink)> links = world.LinksAround(id);
        List<int> removedPrototypes = world.RemoveTree(id);
        world.MarkChanged();
        if (session is not null)
        {
            session.Logic.Evaluate();
            session.Teams.Update();
            UnregisterWorldObject.Broadcast(session, id, removedPrototypes, null, links);
        }
        return true;
    }

    public bool Link(int from, int to) => Connect(from, to, objectLink: false);

    public bool Unlink(int from, int to) => Disconnect(from, to, objectLink: false);

    public bool LinkObjects(int from, int to) => Connect(from, to, objectLink: true);

    public bool UnlinkObjects(int from, int to) => Disconnect(from, to, objectLink: true);

    bool Connect(int from, int to, bool objectLink)
    {
        if (from == to || world.Find(from) is null || world.Find(to) is null) return false;
        var link = world.AddLink(from, to, objectLink);
        world.MarkChanged();
        if (session is null) return true;

        session.Logic.Evaluate();
        SendAll(new EventData((byte)(objectLink ? EventCode.AddObjectLink : EventCode.AddLink))
        {
            Parameters =
            {
                [(byte)ParameterKey.LinkID] = link.Id,
                [(byte)ParameterKey.LinkFromID] = link.From,
                [(byte)ParameterKey.LinkToID] = link.To,
            },
        });
        return true;
    }

    bool Disconnect(int from, int to, bool objectLink)
    {
        List<Link> removed = world.RemoveLinks(from, to, objectLink);
        if (removed.Count == 0) return false;
        world.MarkChanged();
        if (session is null) return true;

        session.Logic.Evaluate();
        foreach (Link link in removed)
            SendAll(new EventData((byte)(objectLink ? EventCode.RemoveObjectLink : EventCode.RemoveLink)) { Parameters = { [(byte)ParameterKey.LinkID] = link.Id } });
        return true;
    }

    void SendAll(EventData evt)
    {
        if (session is null) return;
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }

    public bool Move(int id, Vector3 position, Quaternion? rotation = null, float seconds = 0)
    {
        float[] from = [], turnedFrom = [];
        WorldObject? moved = null;
        bool found = world.Modify(id, obj =>
        {
            (from, turnedFrom) = (obj.Position, obj.Rotation);
            obj.Position = [position.X, position.Y, position.Z];
            if (rotation is Quaternion turn) obj.Rotation = [turn.X, turn.Y, turn.Z, turn.W];
            moved = obj;
        });
        if (!found || moved is null) return false;
        world.MarkChanged();

        if (session?.Players.FirstOrDefault() is not { } anyone) return true;
        int now = anyone.Peer.ServerTime;
        bool glide = seconds > 0;
        if (glide) Send(id, Transform(id, from, turnedFrom, now, TransformPackage.Teleport));
        Send(id, Transform(id, moved.Position, moved.Rotation, glide ? now + (int)(seconds * 1000) : now, glide ? TransformPackage.Stop : TransformPackage.Teleport));
        return true;
    }

    static EventData Transform(int id, float[] position, float[] rotation, int time, TransformPackage type) => new((byte)EventCode.UpdateWorldObject)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = id,
            [(byte)ParameterKey.ActorNr] = 0,
            [(byte)ParameterKey.Timestamp] = time,
            [(byte)ParameterKey.PosX] = position[0],
            [(byte)ParameterKey.PosY] = position[1],
            [(byte)ParameterKey.PosZ] = position[2],
            [(byte)ParameterKey.ByteRotation] = QuaternionCompression.ToBytes(rotation[0], rotation[1], rotation[2], rotation[3]),
            [(byte)ParameterKey.TransformPackageType] = (byte)type,
        },
    };

    public bool SetData(int id, string key, object value) => SetData(id, new Dictionary<string, object> { [key] = value });

    public bool SetSound(int id, ISound sound) =>
        Content.CustomContent.Sounds.Find(sound.Id) is { } known && SetData(id, "url", known.Url);

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

sealed class CubesEditor(GameWorld world, Session? session, int? objectId) : ICubeModel
{
    const int MaxFillCubes = 500_000;

    public int ObjectId => Target()?.Id ?? objectId ?? 0;

    public float CubeSize => Prototype()?.Scale ?? 0f;

    public bool Shared => Target()?.PrototypeId is int id && world.PrototypeUsers(id) > 1;

    public int Count => Prototype()?.Cubes.Count ?? 0;

    public Cube? Get(int x, int y, int z) => Prototype()?.Cubes.Get(x, y, z) is byte[] cube ? CubeEdit.ToCube(cube) : null;

    public IReadOnlyList<(int X, int Y, int Z)> Positions() =>
        Prototype()?.Cubes.Positions().ConvertAll(cube => ((int)cube.X, (int)cube.Y, (int)cube.Z)) ?? [];

    public void Set(int x, int y, int z, byte material)
    {
        using ICubeEdit edit = Edit();
        edit.Set(x, y, z, (Api.Material)material);
    }

    public void Set(int x, int y, int z, Cube cube)
    {
        using ICubeEdit edit = Edit();
        edit.Set(x, y, z, cube);
    }

    public void Remove(int x, int y, int z)
    {
        using ICubeEdit edit = Edit();
        edit.Remove(x, y, z);
    }

    public int Fill(int x1, int y1, int z1, int x2, int y2, int z2, byte material)
    {
        (int fromX, int toX) = Order(x1, x2);
        (int fromY, int toY) = Order(y1, y2);
        (int fromZ, int toZ) = Order(z1, z2);
        long total = (long)(toX - fromX + 1) * (toY - fromY + 1) * (toZ - fromZ + 1);
        if (total > MaxFillCubes) throw new ArgumentException($"fill of {total} cubes is larger than {MaxFillCubes}");

        using ICubeEdit edit = Edit();
        for (int x = fromX; x <= toX; x++)
            for (int y = fromY; y <= toY; y++)
                for (int z = fromZ; z <= toZ; z++)
                    edit.Set(x, y, z, (Api.Material)material);
        return (int)total;
    }

    public void Clear()
    {
        if (Prototype() is not { } prototype) return;
        using ICubeEdit edit = Edit();
        foreach ((short x, short y, short z) in prototype.Cubes.Positions())
            edit.Remove(x, y, z);
    }

    public (int X, int Y, int Z) CubeAt(Vector3 position)
    {
        WorldObject target = Target() ?? throw new InvalidOperationException(Missing);
        Matrix4x4.Invert(CollisionWorld.Local(target, world) * world.WorldMatrix(target.ParentId), out Matrix4x4 toCubes);
        Vector3 cube = Vector3.Transform(position, toCubes);
        return ((int)MathF.Floor(cube.X + 0.5f), (int)MathF.Floor(cube.Y + 0.5f), (int)MathF.Floor(cube.Z + 0.5f));
    }

    public ICubeEdit Edit() => new CubeEdit(world, session, Prototype() ?? throw new InvalidOperationException(Missing));

    public bool MakeUnique()
    {
        if (Target() is not { PrototypeId: int id } target) return false;
        if (world.PrototypeUsers(id) <= 1) return true;
        if (world.MakeUniquePrototype(target.Id) is not Prototype unique) return false;
        world.MarkChanged();
        if (session is not null) RequestWoUniquePrototype.Announce(session, target.Id, unique);
        return true;
    }

    WorldObject? Target() => objectId is int id
        ? world.Find(id) is { PrototypeId: not null } model ? model : null
        : world.FindFirst(WorldObjectType.CubeModelPrototypeTerrain);

    string Missing => objectId is int id ? $"there is no model {id}" : "this world has no terrain";

    Prototype? Prototype() => Target()?.PrototypeId is int id ? world.FindPrototype(id) : null;

    static (int From, int To) Order(int a, int b) => a <= b ? (a, b) : (b, a);
}
