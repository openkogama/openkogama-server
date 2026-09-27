namespace OpenKogama.World;

using Pairs = List<(string Key, PackedType Type, object Value)>;

public static class BlueprintMaps
{
    const string BlueprintData = "BlueprintData";
    const string ChildrenMap = "ChildrenMap";

    public static Pairs Remap(Pairs data, IReadOnlyDictionary<int, int> ids) =>
        [.. data.Select(pair => pair.Key == BlueprintData && pair.Value is Pairs blueprint
            ? (pair.Key, pair.Type, (object)Children(blueprint, children => [.. children.Select(child =>
                child.Value is int id && ids.TryGetValue(id, out int mapped) ? (child.Key, child.Type, (object)mapped) : child)]))
            : pair)];

    public static int Repair(IReadOnlyList<WorldObject> objects)
    {
        var byId = objects.ToDictionary(obj => obj.Id);
        ILookup<int, WorldObject> childrenOf = objects.ToLookup(obj => obj.ParentId);
        int repaired = 0;

        foreach (WorldObject obj in objects)
        {
            if (obj.Data.Find(pair => pair.Key == BlueprintData).Value is not Pairs blueprint
                || blueprint.Find(pair => pair.Key == ChildrenMap).Value is not Pairs map) continue;

            var own = childrenOf[obj.Id].ToList();
            var used = map.Select(entry => entry.Value).OfType<int>().Where(id => own.Exists(child => child.Id == id)).ToHashSet();
            bool changed = false;
            Pairs fixedMap = [.. map.Select(entry =>
            {
                if (entry.Value is not int id || own.Exists(child => child.Id == id)) return entry;
                WorldObject? original = byId.GetValueOrDefault(id);
                var free = own.Where(child => !used.Contains(child.Id)).ToList();
                WorldObject? match = original is null
                    ? free.Count == 1 ? free[0] : null
                    : free.Find(child => child.Type == original.Type && child.PrototypeId == original.PrototypeId);
                if (match is null) return entry;
                used.Add(match.Id);
                changed = true;
                return (entry.Key, entry.Type, (object)match.Id);
            })];

            if (!changed) continue;
            int index = obj.Data.FindIndex(pair => pair.Key == BlueprintData);
            obj.Data[index] = (BlueprintData, obj.Data[index].Type, Children(blueprint, _ => fixedMap));
            repaired++;
        }
        return repaired;
    }

    static Pairs Children(Pairs blueprint, Func<Pairs, Pairs> change) =>
        [.. blueprint.Select(entry => entry.Key == ChildrenMap && entry.Value is Pairs children ? (entry.Key, entry.Type, (object)change(children)) : entry)];
}
