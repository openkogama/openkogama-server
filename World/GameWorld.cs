using System.Numerics;
using System.Text.Json.Nodes;

namespace OpenKogama.World;

public sealed class GameWorld
{
    readonly object _sync = new();
    readonly Dictionary<int, Prototype> _prototypes = [];
    readonly List<WorldObject> _objects = [];
    readonly Dictionary<int, WorldObject> _byId = [];
    readonly List<Link> _links = [];
    readonly List<Link> _objectLinks = [];
    readonly List<byte[]> _runtimeEvents = [];
    int _nextObjectId = 1;
    int _nextPrototypeId = 1;
    int _nextLinkId = 1;
    int _nextObjectLinkId = 1;
    int _changed;
    int _logicVersion;

    public int LogicVersion
    {
        get { lock (_sync) return _logicVersion; }
    }

    public string Name { get; private set; } = "";
    public float[] Spawn { get; private set; } = [0f, 2f, 0f];

    public int NewObjectId()
    {
        lock (_sync) return _nextObjectId++;
    }

    public int NewPrototypeId()
    {
        lock (_sync) return _nextPrototypeId++;
    }

    public int NewLinkId()
    {
        lock (_sync) return _nextLinkId++;
    }

    public void Add(Prototype prototype)
    {
        lock (_sync)
        {
            _prototypes[prototype.Id] = prototype;
            _nextPrototypeId = Math.Max(_nextPrototypeId, prototype.Id + 1);
        }
    }

    public void Add(WorldObject obj)
    {
        RuntimeDefaults.Apply(obj);
        lock (_sync)
        {
            Store(obj);
            _nextObjectId = Math.Max(_nextObjectId, obj.Id + 1);
        }
    }

    public List<WorldObject> Subtree(int id)
    {
        lock (_sync) return Subtree(id, _objects.ToLookup(obj => obj.ParentId));
    }

    List<WorldObject> Subtree(int id, ILookup<int, WorldObject> children)
    {
        var result = new List<WorldObject>();
        if (Get(id) is WorldObject root) Visit(root);
        return result;

        void Visit(WorldObject obj)
        {
            result.Add(obj);
            foreach (WorldObject child in children[obj.Id].OrderBy(child => child.Id))
                Visit(child);
        }
    }

    void Store(WorldObject obj)
    {
        _logicVersion++;
        _objects.Add(obj);
        _byId[obj.Id] = obj;
    }

    WorldObject? Get(int id) => _byId.GetValueOrDefault(id);

    public void Remove(int id)
    {
        lock (_sync)
        {
            var ids = Subtree(id).Select(obj => obj.Id).ToHashSet();
            _logicVersion++;
            _objects.RemoveAll(obj => ids.Contains(obj.Id));
            foreach (int removed in ids) _byId.Remove(removed);
            _links.RemoveAll(link => ids.Contains(link.From) || ids.Contains(link.To));
            _objectLinks.RemoveAll(link => ids.Contains(link.From) || ids.Contains(link.To));
        }
    }

    public List<(int Id, bool ObjectLink)> LinksAround(int id)
    {
        lock (_sync)
        {
            var ids = Subtree(id).Select(obj => obj.Id).ToHashSet();
            bool Touches(Link link) => ids.Contains(link.From) || ids.Contains(link.To);
            return [.. _links.Where(Touches).Select(link => (link.Id, false)), .. _objectLinks.Where(Touches).Select(link => (link.Id, true))];
        }
    }

    public Link AddLink(int from, int to, bool objectLink)
    {
        lock (_sync)
        {
            _logicVersion++;
            var link = new Link(objectLink ? _nextObjectLinkId++ : _nextLinkId++, from, to);
            (objectLink ? _objectLinks : _links).Add(link);
            return link;
        }
    }

    public List<int> LogicChunk(int id)
    {
        lock (_sync)
        {
            var chunk = new HashSet<int> { id };
            var pending = new Queue<int>([id]);
            while (pending.TryDequeue(out int current))
                foreach (Link link in _links.Where(link => link.From == current || link.To == current))
                    foreach (int next in new[] { link.From, link.To })
                        if (chunk.Add(next)) pending.Enqueue(next);
            return [.. chunk];
        }
    }

    public bool RemoveLink(int id, bool objectLink)
    {
        lock (_sync)
        {
            _logicVersion++;
            return (objectLink ? _objectLinks : _links).RemoveAll(link => link.Id == id) > 0;
        }
    }

    public List<int> RemoveTree(int id)
    {
        lock (_sync)
        {
            List<int> candidates = [.. Subtree(id).Select(obj => obj.PrototypeId).OfType<int>().Distinct()];
            Remove(id);

            var stillUsed = _objects.Select(obj => obj.PrototypeId).OfType<int>().ToHashSet();
            List<int> unused = [.. candidates.Where(prototype => !stillUsed.Contains(prototype))];
            foreach (int prototype in unused) _prototypes.Remove(prototype);
            return unused;
        }
    }

    public int PrototypeUsers(int prototypeId)
    {
        lock (_sync) return _objects.Count(obj => obj.PrototypeId == prototypeId);
    }

    public int? FirstUser(int prototypeId)
    {
        lock (_sync) return _objects.Find(obj => obj.PrototypeId == prototypeId)?.Id;
    }

    public Prototype? MakeUniquePrototype(int objectId)
    {
        lock (_sync)
        {
            WorldObject? obj = Get(objectId);
            if (obj?.PrototypeId is not int oldId || !_prototypes.TryGetValue(oldId, out Prototype? old)) return null;

            var unique = new Prototype(_nextPrototypeId++, old.Scale, old.AuthorId, old.Cubes.Clone());
            _prototypes[unique.Id] = unique;

            obj.Data = [.. obj.Data.Select(pair => pair.Key == "protoTypeID" ? (pair.Key, pair.Type, (object)unique.Id) : pair)];
            return unique;
        }
    }

    public Snapshot Insert(Snapshot item, int parentId)
    {
        lock (_sync)
        {
            if (Get(parentId) is not { } parent || Leaves.Contains(parent.Type)) parentId = RootId;
            var prototypeIds = item.Prototypes.ToDictionary(p => p.Id, _ => _nextPrototypeId++);
            var objectIds = item.Objects.ToDictionary(o => o.Id, _ => _nextObjectId++);

            List<Prototype> prototypes = [.. item.Prototypes.Select(p =>
                new Prototype(prototypeIds[p.Id], p.Scale, p.AuthorId, p.Cubes))];

            List<WorldObject> objects = [.. item.Objects.Select(o => new WorldObject
            {
                Id = objectIds[o.Id],
                ParentId = objectIds.GetValueOrDefault(o.ParentId, parentId),
                ItemId = o.ItemId,
                Type = o.Type,
                Position = o.Position,
                Rotation = o.Rotation,
                Scale = o.Scale,
                Data = Remap(o.Data, prototypeIds, objectIds),
                Owner = o.Owner,
                PreviewOwner = o.PreviewOwner,
                Runtime = o.Runtime,
            })];

            List<Link> links = [.. item.Links.Select(l => new Link(_nextLinkId++, objectIds[l.From], objectIds[l.To]))];
            List<Link> objectLinks = [.. item.ObjectLinks.Select(l => new Link(_nextObjectLinkId++, objectIds[l.From], objectIds[l.To]))];

            foreach (Prototype prototype in prototypes) _prototypes[prototype.Id] = prototype;
            objects.ForEach(RuntimeDefaults.Apply);
            objects.ForEach(Store);
            _links.AddRange(links);
            _objectLinks.AddRange(objectLinks);

            return new Snapshot(prototypes, objects, links, objectLinks);
        }
    }

    static List<(string Key, PackedType Type, object Value)> Remap(
        List<(string Key, PackedType Type, object Value)> data,
        Dictionary<int, int> prototypeIds,
        Dictionary<int, int> objectIds) =>
    [
        .. data.Select(pair => pair switch
        {
            ("protoTypeID", PackedType.Int32, int id) when prototypeIds.TryGetValue(id, out int newId) =>
                (pair.Key, pair.Type, (object)newId),
            ("CubeModelId", PackedType.Int32, int modelId) when objectIds.TryGetValue(modelId, out int newModelId) =>
                (pair.Key, pair.Type, (object)newModelId),
            ("ChildrenMap", PackedType.Hashtable, List<(string, PackedType, object)> children) =>
                (pair.Key, pair.Type, (object)children.Select(child => child is (_, PackedType.Int32, int childId)
                    ? (child.Item1, child.Item2, (object)objectIds.GetValueOrDefault(childId, childId))
                    : child).ToList()),
            (_, PackedType.Hashtable, List<(string, PackedType, object)> nested) =>
                (pair.Key, pair.Type, (object)Remap(nested, prototypeIds, objectIds)),
            _ => pair,
        }),
    ];

    public WorldObject? CloneTree(int id) => CloneTree(id, out _, out _);

    public WorldObject? CloneTree(int id, out int firstLinkId, out int firstObjectLinkId)
    {
        lock (_sync)
        {
            firstLinkId = _nextLinkId;
            firstObjectLinkId = _nextObjectLinkId;
            List<WorldObject> originals = Subtree(id);
            if (originals.Count == 0) return null;

            var newIds = new Dictionary<int, int>();
            foreach (WorldObject original in originals)
                newIds[original.Id] = _nextObjectId++;

            foreach (WorldObject original in originals)
            {
                Store(new WorldObject
                {
                    Id = newIds[original.Id],
                    ParentId = original.Id == id ? RootId : newIds[original.ParentId],
                    ItemId = original.ItemId,
                    Type = original.Type,
                    Position = [.. original.Position],
                    Rotation = [.. original.Rotation],
                    Scale = [.. original.Scale],
                    Data = BlueprintMaps.Remap(original.Data, newIds),
                    Owner = original.Owner,
                    PreviewOwner = original.PreviewOwner,
                    Runtime = [.. original.Runtime],
                });
            }

            ILookup<int, Link> inputs = _links.ToLookup(link => link.To);
            ILookup<int, Link> outputs = _links.ToLookup(link => link.From);
            ILookup<int, Link> objectLinks = _objectLinks
                .SelectMany(link => new[] { (End: link.From, Link: link), (End: link.To, Link: link) })
                .ToLookup(pair => pair.End, pair => pair.Link);
            foreach (Link link in Inside(originals.SelectMany(obj => inputs[obj.Id].Concat(outputs[obj.Id])), newIds))
                _links.Add(new Link(_nextLinkId++, newIds[link.From], newIds[link.To]));
            foreach (Link link in Inside(originals.SelectMany(obj => objectLinks[obj.Id]), newIds))
                _objectLinks.Add(new Link(_nextObjectLinkId++, newIds[link.From], newIds[link.To]));

            return Get(newIds[id]);
        }
    }

    static List<Link> Inside(IEnumerable<Link> links, Dictionary<int, int> subtree)
    {
        var seen = new HashSet<int>();
        return [.. links.Where(link => seen.Add(link.Id) && subtree.ContainsKey(link.From) && subtree.ContainsKey(link.To))];
    }

    public bool SetPrototypeScale(int prototypeId, float scale)
    {
        lock (_sync)
        {
            if (!_prototypes.TryGetValue(prototypeId, out Prototype? prototype)) return false;

            _prototypes[prototypeId] = new Prototype(prototypeId, scale, prototype.AuthorId, prototype.Cubes);
            foreach (WorldObject obj in _objects.Where(o => o.PrototypeId == prototypeId))
                obj.Scale = [scale, scale, scale];
            return true;
        }
    }

    public bool Reparent(int id, int parentId)
    {
        lock (_sync)
        {
            WorldObject? obj = Get(id);
            if (obj is null || !_byId.ContainsKey(parentId) || Subtree(id).Any(o => o.Id == parentId)) return false;

            Matrix4x4.Invert(WorldMatrix(parentId), out Matrix4x4 toParent);
            Matrix4x4.Decompose(WorldMatrix(id) * toParent, out Vector3 scale, out Quaternion rotation, out Vector3 position);

            obj.ParentId = parentId;
            obj.Position = [position.X, position.Y, position.Z];
            obj.Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W];
            obj.Scale = [scale.X, scale.Y, scale.Z];
            return true;
        }
    }

    public bool SetParent(int id, int parentId)
    {
        lock (_sync)
        {
            WorldObject? obj = Get(id);
            if (obj is null || obj.ParentId == -1 || !_byId.ContainsKey(parentId) || Subtree(id).Any(o => o.Id == parentId)) return false;
            obj.ParentId = parentId;
            return true;
        }
    }

    public Matrix4x4 WorldMatrix(int id)
    {
        lock (_sync)
        {
            Matrix4x4 matrix = Matrix4x4.Identity;
            for (WorldObject? obj = Get(id); obj is not null; obj = Get(obj.ParentId))
            {
                matrix *= Matrix4x4.CreateScale(obj.Scale[0], obj.Scale[1], obj.Scale[2])
                    * Matrix4x4.CreateFromQuaternion(new Quaternion(obj.Rotation[0], obj.Rotation[1], obj.Rotation[2], obj.Rotation[3]))
                    * Matrix4x4.CreateTranslation(obj.Position[0], obj.Position[1], obj.Position[2]);
            }
            return matrix;
        }
    }

    public bool Modify(int id, Action<WorldObject> change)
    {
        lock (_sync)
        {
            WorldObject? obj = Get(id);
            if (obj is null) return false;
            change(obj);
            return true;
        }
    }

    public WorldObject? Find(int id)
    {
        lock (_sync) return Get(id);
    }

    static readonly HashSet<WorldObjectType> Leaves = [WorldObjectType.CubeModel, WorldObjectType.PointLight, WorldObjectType.SoundEmitter];

    public int RootId
    {
        get { lock (_sync) return _objects.Find(obj => obj.ParentId == -1)?.Id ?? -1; }
    }

    public bool HasRootGroup
    {
        get { lock (_sync) return _objects.Find(obj => obj.ParentId == -1) is { Type: WorldObjectType.Group }; }
    }

    public WorldObject? FindFirst(WorldObjectType type)
    {
        lock (_sync) return _objects.Find(obj => obj.Type == type);
    }

    public List<WorldObject> FindAll(WorldObjectType type)
    {
        lock (_sync) return _objects.FindAll(obj => obj.Type == type);
    }

    public Prototype? FindPrototype(int id)
    {
        lock (_sync) return _prototypes.GetValueOrDefault(id);
    }

    public void MarkChanged() => Interlocked.Exchange(ref _changed, 1);

    public bool TakeChanged() => Interlocked.Exchange(ref _changed, 0) == 1;

    public Snapshot LogicGraph()
    {
        lock (_sync)
        {
            var ids = _links.SelectMany(link => new[] { link.From, link.To }).ToHashSet();
            return new Snapshot([], [.. ids.Select(Get).OfType<WorldObject>()], [.. _links], []);
        }
    }

    public Snapshot ToSnapshot()
    {
        lock (_sync)
            return new Snapshot([.. _prototypes.Values], Ordered(), [.. _links], [.. _objectLinks])
            {
                RuntimeEvents = [.. _runtimeEvents],
            };
    }

    public void AddRuntimeEvent(byte[] runtimeEvent)
    {
        lock (_sync) _runtimeEvents.Add(runtimeEvent);
        MarkChanged();
    }

    public List<byte[]> RuntimeEventsFrom(int index)
    {
        lock (_sync) return [.. _runtimeEvents.Skip(index)];
    }

    public int RuntimeEventCount
    {
        get { lock (_sync) return _runtimeEvents.Count; }
    }

    public void ClearRuntimeEvents()
    {
        lock (_sync) _runtimeEvents.Clear();
        MarkChanged();
    }

    List<WorldObject> Ordered()
    {
        ILookup<int, WorldObject> children = _objects.ToLookup(obj => obj.ParentId);
        var ordered = new List<WorldObject>(_objects.Count);
        foreach (WorldObject top in _objects.Where(obj => !_byId.ContainsKey(obj.ParentId)))
            ordered.AddRange(Subtree(top.Id, children));
        return ordered;
    }

    Snapshot ToSaveSnapshot(bool runtimeEvents = false)
    {
        lock (_sync)
        {
            List<WorldObject> all = Ordered();
            var skipped = new HashSet<int>();
            foreach (WorldObject obj in all)
                if (obj.Type is WorldObjectType.Avatar or WorldObjectType.BuildModeAvatar || obj.Transient || skipped.Contains(obj.ParentId))
                    skipped.Add(obj.Id);

            List<WorldObject> objects = [.. all.Where(obj => !skipped.Contains(obj.Id))];
            return new Snapshot(UsedPrototypes(objects), objects, [.. _links], [.. _objectLinks])
            {
                RuntimeEvents = runtimeEvents ? TerrainEvents.Compact(_runtimeEvents) : [],
            };
        }
    }

    public Snapshot SubtreeSnapshot(int id)
    {
        lock (_sync)
        {
            List<WorldObject> objects = Subtree(id);
            return new Snapshot(UsedPrototypes(objects), objects, [], []);
        }
    }

    List<Prototype> UsedPrototypes(IEnumerable<WorldObject> objects)
    {
        var used = objects.Select(obj => obj.PrototypeId).OfType<int>().ToHashSet();
        return [.. _prototypes.Values.Where(prototype => used.Contains(prototype.Id))];
    }

    public byte[] ToData() => WorldSerializer.Write(ToSaveSnapshot());

    public byte[] ToPlayData() => WorldSerializer.Write(ToSaveSnapshot(runtimeEvents: true));

    public void Save(string path)
    {
        var meta = new JsonObject
        {
            ["GameTitle"] = Name,
            ["SavedAt"] = DateTime.UtcNow.ToString("O"),
            ["Kgmexporter"] = "openkogama-server",
            ["Spawn"] = new JsonArray(Spawn.Select(v => (JsonNode)v).ToArray()),
        };

        KgmapFile.Write(path, meta, ToData());
    }

    public static GameWorld Load(string path, Func<WorldObject, bool>? keep = null) =>
        Read(File.ReadAllBytes(path), keep);

    public static GameWorld Read(byte[] file, Func<WorldObject, bool>? keep = null)
    {
        if (!KgmapFile.IsKgmap(file)) return FromData("", null, [file], keep);

        (JsonObject meta, List<byte[]> batches) = KgmapFile.Read(file);
        float[]? spawn = meta["Spawn"] is JsonArray values ? [.. values.Select(v => v!.GetValue<float>())] : null;
        return FromData(meta["GameTitle"]?.GetValue<string>() ?? "", spawn, batches, keep);
    }

    public static GameWorld FromData(string name, float[]? spawn, IEnumerable<byte[]> batches, Func<WorldObject, bool>? keep = null, bool runtimeEvents = false)
    {
        var world = new GameWorld { Name = name };

        var objects = new List<WorldObject>();
        foreach (byte[] batch in batches)
        {
            Snapshot snapshot = WorldSerializer.Read(batch, events: runtimeEvents);
            foreach (Prototype prototype in snapshot.Prototypes) world.Add(prototype);
            world._runtimeEvents.AddRange(snapshot.RuntimeEvents);
            objects.AddRange(snapshot.Objects);
            world._links.AddRange(snapshot.Links);
            world._objectLinks.AddRange(snapshot.ObjectLinks);
        }

        foreach (WorldObject obj in objects.Where(obj => obj.Type == WorldObjectType.SpawnPoint))
            obj.Type = WorldObjectType.SpawnPointBlue;

        foreach (WorldObject obj in objects.Where(obj => obj.Type == WorldObjectType.PointLight))
        {
            int halo = obj.Data.FindIndex(pair => pair.Key == "halo");
            if (halo >= 0 && obj.Data[halo].Value is int value && value is < 1 or > 3)
                obj.Data[halo] = ("halo", obj.Data[halo].Type, 1);
        }

        world.Spawn = spawn
            ?? objects.Find(obj => obj.Type == WorldObjectType.SpawnPointBlue)?.Position
            ?? world.Spawn;

        int root = (objects.Find(obj => obj.ParentId == -1 && obj.Type == WorldObjectType.Group) ?? objects.Find(obj => obj.ParentId == -1))?.Id ?? -1;
        int extraRoots = objects.Count(obj => obj.ParentId == -1 && obj.Id != root);
        var kept = new HashSet<int>();
        foreach (WorldObject obj in objects)
        {
            bool parentKept = obj.ParentId == -1 ? obj.Id == root : kept.Contains(obj.ParentId);
            if (!parentKept || keep?.Invoke(obj) == false) continue;

            kept.Add(obj.Id);
            world.Add(obj);
        }

        world._links.RemoveAll(link => !kept.Contains(link.From) || !kept.Contains(link.To));
        world._objectLinks.RemoveAll(link => !kept.Contains(link.From) || !kept.Contains(link.To));
        world._nextLinkId = world._links.Select(link => link.Id + 1).DefaultIfEmpty(1).Max();
        world._nextObjectLinkId = world._objectLinks.Select(link => link.Id + 1).DefaultIfEmpty(1).Max();

        var used = world.UsedPrototypes(world._objects).Select(prototype => prototype.Id).ToHashSet();
        foreach (int id in world._prototypes.Keys.Where(id => !used.Contains(id)).ToList())
            world._prototypes.Remove(id);

        if (extraRoots > 0)
        {
            world.MarkChanged();
            Console.WriteLine($"dropped {extraRoots} objects placed outside the world's root");
        }

        int repaired = BlueprintMaps.Repair(world._objects);
        if (repaired > 0)
        {
            world.MarkChanged();
            Console.WriteLine($"repaired {repaired} cloned blueprints");
        }

        return world;
    }
}
