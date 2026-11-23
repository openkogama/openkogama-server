using OpenKogama.Kogama.Protocols;

namespace OpenKogama.World;

public static class WorldConverter
{
    static readonly HashSet<int> Supported2015 = [.. Enumerable.Range(1, 64), 145, 146, 148, 149];

    public static GameWorld Load(string path) => Convert(keep => GameWorld.Load(path, keep), Is2015, out _);

    public static GameWorld LoadMap(string path)
    {
        GameWorld world = Convert(keep => GameWorld.Load(path, keep), KnownByAnyVersion(), out Dictionary<int, int> dropped);
        if (dropped.Count > 0)
            Console.WriteLine($"{Path.GetFileName(path)}: dropped {string.Join(", ", dropped.Select(pair => $"type {pair.Key} x{pair.Value}"))}");
        return world;
    }

    public static GameWorld Import(byte[] file, string? client, out Dictionary<int, int> dropped)
    {
        GameWorld world = Convert(keep => GameWorld.Read(file, keep), KnownByAnyVersion(), out dropped);
        foreach (WorldObject obj in world.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.TextMsg))
            world.Modify(obj.Id, text => TextEras.ForClient(text, client));
        return world;
    }

    static Func<WorldObject, bool> KnownByAnyVersion()
    {
        List<ProtocolTable> tables = [.. ProtocolTable.Native()];
        var known = new Dictionary<(WorldObjectType, object?), bool>();
        return obj =>
        {
            if (obj.Type is WorldObjectType.Avatar or WorldObjectType.BuildModeAvatar) return false;
            var key = (obj.Type, obj.Data.Find(pair => pair.Key == "itemType").Value);
            if (!known.TryGetValue(key, out bool result))
                known[key] = result = tables.Any(table => Game.Items.KnownBy(obj, table));
            return result;
        };
    }

    static bool Is2015(WorldObject obj) => Supported2015.Contains((int)obj.Type);

    static GameWorld Convert(Func<Func<WorldObject, bool>, GameWorld> read, Func<WorldObject, bool> supported, out Dictionary<int, int> dropped)
    {
        var skipped = new Dictionary<int, int>();
        GameWorld world = read(obj =>
        {
            if (supported(obj)) return true;
            skipped[(int)obj.Type] = skipped.GetValueOrDefault((int)obj.Type) + 1;
            return false;
        });

        IReadOnlyList<WorldObject> objects = world.ToSnapshot().Objects;
        HashSet<int> broken = LegacyWorld.WithBrokenBlueprints(objects, []);
        foreach (WorldObject obj in objects.Where(obj => broken.Contains(obj.Id)))
        {
            skipped[(int)obj.Type] = skipped.GetValueOrDefault((int)obj.Type) + 1;
            if (!broken.Contains(obj.ParentId)) world.Remove(obj.Id);
        }

        foreach (WorldObject obj in world.ToSnapshot().Objects)
        {
            world.Modify(obj.Id, o =>
            {
                o.Runtime.Clear();
                o.Owner = null;
                o.PreviewOwner = null;
                RuntimeDefaults.Apply(o);
            });
        }

        dropped = skipped;
        return world;
    }
}
