using System.Numerics;
using System.Text.Json.Nodes;

namespace OpenKogama.World;

public sealed class GameWorld
{
    readonly object _sync = new();
    readonly Dictionary<int, Prototype> _prototypes = [];
    readonly List<WorldObject> _objects = [];
    readonly List<Link> _links = [];
    readonly List<Link> _objectLinks = [];
    readonly List<byte[]> _runtimeEvents = [];
    int _nextObjectId = 1;
    int _nextPrototypeId = 1;
    int _nextLinkId = 1;
    int _nextObjectLinkId = 1;
    int _changed;

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
            _objects.Add(obj);
            _nextObjectId = Math.Max(_nextObjectId, obj.Id + 1);
        }
    }

    public List<WorldObject> Subtree(int id)
    {
        lock (_sync)
        {
            var result = new List<WorldObject>();
            WorldObject? root = _objects.Find(obj => obj.Id == id);
            if (root is not null) Visit(root);
            return result;

            void Visit(WorldObject obj)
            {
                result.Add(obj);
                foreach (WorldObject child in _objects.Where(o => o.ParentId == obj.Id).ToList())
                    Visit(child);
            }
        }
    }

    public void Remove(int id)
    {
        lock (_sync)
        {
            var ids = Subtree(id).Select(obj => obj.Id).ToHashSet();
            _objects.RemoveAll(obj => ids.Contains(obj.Id));
            _links.RemoveAll(link => ids.Contains(link.From) || ids.Contains(link.To));
            _objectLinks.RemoveAll(link => ids.Contains(link.From) || ids.Contains(link.To));
        }
    }

    public Link AddLink(int from, int to, bool objectLink)
    {
        lock (_sync)
        {
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
        lock (_sync) return (objectLink ? _objectLinks : _links).RemoveAll(link => link.Id == id) > 0;
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

    public Prototype? MakeUniquePrototype(int objectId)
    {
        lock (_sync)
        {
            WorldObject? obj = _objects.Find(o => o.Id == objectId);
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
            _objects.AddRange(objects);
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
            ("ChildrenMap", PackedType.Hashtable, List<(string, PackedType, object)> children) =>
                (pair.Key, pair.Type, (object)children.Select(child => child is (_, PackedType.Int32, int childId)
                    ? (child.Item1, child.Item2, (object)objectIds.GetValueOrDefault(childId, childId))
                    : child).ToList()),
            (_, PackedType.Hashtable, List<(string, PackedType, object)> nested) =>
                (pair.Key, pair.Type, (object)Remap(nested, prototypeIds, objectIds)),
            _ => pair,
        }),
    ];

    public WorldObject? CloneTree(int id)
    {
        lock (_sync)
        {
            List<WorldObject> originals = Subtree(id);
            if (originals.Count == 0) return null;

            var newIds = new Dictionary<int, int>();
            foreach (WorldObject original in originals)
                newIds[original.Id] = _nextObjectId++;

            foreach (WorldObject original in originals)
            {
                _objects.Add(new WorldObject
                {
                    Id = newIds[original.Id],
                    ParentId = original.Id == id ? RootId : newIds[original.ParentId],
                    ItemId = original.ItemId,
                    Type = original.Type,
                    Position = [.. original.Position],
                    Rotation = [.. original.Rotation],
                    Scale = [.. original.Scale],
                    Data = [.. original.Data],
                    Owner = original.Owner,
                    PreviewOwner = original.PreviewOwner,
                    Runtime = [.. original.Runtime],
                });
            }

            return _objects.Find(obj => obj.Id == newIds[id]);
        }
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
            WorldObject? obj = _objects.Find(o => o.Id == id);
            if (obj is null || _objects.All(o => o.Id != parentId) || Subtree(id).Any(o => o.Id == parentId)) return false;

            Matrix4x4.Invert(WorldMatrix(parentId), out Matrix4x4 toParent);
            Matrix4x4.Decompose(WorldMatrix(id) * toParent, out Vector3 scale, out Quaternion rotation, out Vector3 position);

            obj.ParentId = parentId;
            obj.Position = [position.X, position.Y, position.Z];
            obj.Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W];
            obj.Scale = [scale.X, scale.Y, scale.Z];
            return true;
        }
    }

    Matrix4x4 WorldMatrix(int id)
    {
        Matrix4x4 matrix = Matrix4x4.Identity;
        for (WorldObject? obj = _objects.Find(o => o.Id == id); obj is not null; obj = _objects.Find(o => o.Id == obj.ParentId))
        {
            matrix *= Matrix4x4.CreateScale(obj.Scale[0], obj.Scale[1], obj.Scale[2])
                * Matrix4x4.CreateFromQuaternion(new Quaternion(obj.Rotation[0], obj.Rotation[1], obj.Rotation[2], obj.Rotation[3]))
                * Matrix4x4.CreateTranslation(obj.Position[0], obj.Position[1], obj.Position[2]);
        }
        return matrix;
    }

    public bool Modify(int id, Action<WorldObject> change)
    {
        lock (_sync)
        {
            WorldObject? obj = _objects.Find(o => o.Id == id);
            if (obj is null) return false;
            change(obj);
            return true;
        }
    }

    public WorldObject? Find(int id)
    {
        lock (_sync) return _objects.Find(obj => obj.Id == id);
    }

    public int RootId
    {
        get { lock (_sync) return _objects.Find(obj => obj.ParentId == -1)?.Id ?? -1; }
    }

    public WorldObject? FindFirst(WorldObjectType type)
    {
        lock (_sync) return _objects.Find(obj => obj.Type == type);
    }

    public Prototype? FindPrototype(int id)
    {
        lock (_sync) return _prototypes.GetValueOrDefault(id);
    }

    public void MarkChanged() => Interlocked.Exchange(ref _changed, 1);

    public bool TakeChanged() => Interlocked.Exchange(ref _changed, 0) == 1;

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
    }

    public void ClearRuntimeEvents()
    {
        lock (_sync) _runtimeEvents.Clear();
    }

    List<WorldObject> Ordered()
    {
        var ids = _objects.Select(obj => obj.Id).ToHashSet();
        var ordered = new List<WorldObject>(_objects.Count);
        foreach (WorldObject top in _objects.Where(obj => !ids.Contains(obj.ParentId)))
            ordered.AddRange(Subtree(top.Id));
        return ordered;
    }

    Snapshot ToSaveSnapshot()
    {
        lock (_sync)
        {
            List<WorldObject> all = Ordered();
            var skipped = new HashSet<int>();
            foreach (WorldObject obj in all)
                if (obj.Type == WorldObjectType.Avatar || obj.Transient || skipped.Contains(obj.ParentId))
                    skipped.Add(obj.Id);

            List<WorldObject> objects = [.. all.Where(obj => !skipped.Contains(obj.Id))];
            return new Snapshot(UsedPrototypes(objects), objects, [.. _links], [.. _objectLinks]);
        }
    }

    List<Prototype> UsedPrototypes(IEnumerable<WorldObject> objects)
    {
        var used = objects.Select(obj => obj.PrototypeId).OfType<int>().ToHashSet();
        return [.. _prototypes.Values.Where(prototype => used.Contains(prototype.Id))];
    }

    public byte[] ToData() => WorldSerializer.Write(ToSaveSnapshot());

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

    public static GameWorld FromData(string name, float[]? spawn, IEnumerable<byte[]> batches, Func<WorldObject, bool>? keep = null)
    {
        var world = new GameWorld { Name = name };

        var objects = new List<WorldObject>();
        foreach (byte[] batch in batches)
        {
            Snapshot snapshot = WorldSerializer.Read(batch);
            foreach (Prototype prototype in snapshot.Prototypes) world.Add(prototype);
            objects.AddRange(snapshot.Objects);
            world._links.AddRange(snapshot.Links);
            world._objectLinks.AddRange(snapshot.ObjectLinks);
        }

        world.Spawn = spawn
            ?? objects.Find(obj => obj.Type is WorldObjectType.SpawnPointBlue or WorldObjectType.SpawnPoint)?.Position
            ?? world.Spawn;

        var kept = new HashSet<int>();
        foreach (WorldObject obj in objects)
        {
            bool parentKept = obj.ParentId == -1 || kept.Contains(obj.ParentId);
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

        return world;
    }
}
