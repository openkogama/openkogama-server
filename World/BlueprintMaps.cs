namespace OpenKogama.World;

using Pairs = List<(string Key, PackedType Type, object Value)>;

public static class BlueprintMaps
{
    const string BlueprintData = "BlueprintData";
    const string ChildrenMap = "ChildrenMap";
    const string ItemData = "itemData";
    const string CubeModelId = "CubeModelId";
    const string ItemBase = "ItemBase";
    const string ItemCubeModel = "ItemCubeModel";

    public static Pairs Remap(Pairs data, IReadOnlyDictionary<int, int> ids) =>
        [.. data.Select(pair => pair switch
        {
            (BlueprintData, _, Pairs blueprint) =>
                (pair.Key, pair.Type, (object)Children(blueprint, children => [.. children.Select(child => Mapped(child, ids))])),
            (ItemData, _, Pairs item) =>
                (pair.Key, pair.Type, (object)item.Select(entry => entry.Key == CubeModelId ? Mapped(entry, ids) : entry).ToList()),
            _ => pair,
        })];

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
        return repaired + RepairItemModels(objects, byId);
    }

    static int RepairItemModels(IReadOnlyList<WorldObject> objects, Dictionary<int, WorldObject> byId)
    {
        int repaired = 0;
        foreach (WorldObject obj in objects)
        {
            if (obj.Data.Find(pair => pair.Key == BlueprintData).Value is not Pairs blueprint
                || blueprint.Find(pair => pair.Key == ChildrenMap).Value is not Pairs map
                || map.Find(entry => entry.Key == ItemBase).Value is not int itemId
                || map.Find(entry => entry.Key == ItemCubeModel).Value is not int modelId
                || !byId.TryGetValue(itemId, out WorldObject? item)) continue;

            int index = item.Data.FindIndex(pair => pair.Key == ItemData);
            if (index < 0 || item.Data[index].Value is not Pairs itemData) continue;
            int model = itemData.FindIndex(entry => entry.Key == CubeModelId);
            if (model < 0 || itemData[model].Value is int current && current == modelId) continue;

            itemData[model] = (CubeModelId, itemData[model].Type, modelId);
            repaired++;
        }
        return repaired;
    }

    static (string Key, PackedType Type, object Value) Mapped((string Key, PackedType Type, object Value) entry, IReadOnlyDictionary<int, int> ids) =>
        entry.Value is int id && ids.TryGetValue(id, out int mapped) ? (entry.Key, entry.Type, mapped) : entry;

    static Pairs Children(Pairs blueprint, Func<Pairs, Pairs> change) =>
        [.. blueprint.Select(entry => entry.Key == ChildrenMap && entry.Value is Pairs children ? (entry.Key, entry.Type, (object)change(children)) : entry)];
}
