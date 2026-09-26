namespace OpenKogama.World;

public static class WorldConverter
{
    static readonly HashSet<int> Supported2015 = [.. Enumerable.Range(1, 64), 145, 146, 148, 149];

    public static GameWorld Load(string path) => Convert(keep => GameWorld.Load(path, keep), out _);

    public static GameWorld Import(byte[] file, out Dictionary<int, int> dropped) =>
        Convert(keep => GameWorld.Read(file, keep), out dropped);

    static GameWorld Convert(Func<Func<WorldObject, bool>, GameWorld> read, out Dictionary<int, int> dropped)
    {
        var skipped = new Dictionary<int, int>();
        GameWorld world = read(obj =>
        {
            if (Supported2015.Contains((int)obj.Type)) return true;
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
