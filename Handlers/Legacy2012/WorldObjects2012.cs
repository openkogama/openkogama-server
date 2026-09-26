using OpenKogama.Handlers;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Legacy2012;

public static class WorldObjects2012
{
    static readonly Dictionary<WorldObjectType, WorldObjectType> Fallbacks = new()
    {
        [WorldObjectType.SpawnPointBlue] = WorldObjectType.SpawnPoint,
        [WorldObjectType.SpawnPointRed] = WorldObjectType.SpawnPoint,
        [WorldObjectType.SpawnPointGreen] = WorldObjectType.SpawnPoint,
        [WorldObjectType.SpawnPointYellow] = WorldObjectType.SpawnPoint,
    };

    public static List<Dictionary<object, object?>> Describe(IReadOnlyList<WorldObject> objects, ProtocolTable client)
    {
        var types = new CodeMap(ProtocolTable.For(ClientProtocols.ServerVersion).WorldObjectType, client.WorldObjectType);
        int? Type(WorldObject obj) => types.Map((int)Fallbacks.GetValueOrDefault(obj.Type, obj.Type));
        var removed = objects.Where(obj => Type(obj) is null).Select(obj => obj.Id).ToHashSet();
        bool grew = removed.Count > 0;
        while (grew)
        {
            grew = false;
            foreach (WorldObject obj in objects)
                if (removed.Contains(obj.ParentId) && removed.Add(obj.Id))
                    grew = true;
        }

        return [.. objects.Where(obj => !removed.Contains(obj.Id)).Select(obj => new Dictionary<object, object?>
        {
            [(byte)Key2012.WorldObjectType] = Type(obj)!.Value,
            [(byte)Key2012.WorldObjectData] = Table(obj.Data),
            [(byte)Key2012.WorldObjectRunTimeData] = Table(obj.Runtime),
            [(byte)Key2012.WorldObjectID] = obj.Id,
            [(byte)Key2012.WorldObjectGroupID] = obj.ParentId,
            [(byte)Key2012.OwnerActorNr] = obj.Owner ?? 0,
            [(byte)Key2012.PosX] = obj.Position[0],
            [(byte)Key2012.PosY] = obj.Position[1],
            [(byte)Key2012.PosZ] = obj.Position[2],
            [(byte)Key2012.RotX] = obj.Rotation[0],
            [(byte)Key2012.RotY] = obj.Rotation[1],
            [(byte)Key2012.RotZ] = obj.Rotation[2],
            [(byte)Key2012.RotW] = obj.Rotation[3],
            [(byte)Key2012.ScaleX] = obj.Scale[0],
            [(byte)Key2012.ScaleY] = obj.Scale[1],
            [(byte)Key2012.ScaleZ] = obj.Scale[2],
        })];
    }

    public static WorldObject Read(OperationRequest request, int id, ProtocolTable client)
    {
        var types = new CodeMap(client.WorldObjectType, ProtocolTable.For(ClientProtocols.ServerVersion).WorldObjectType);
        int type = Convert.ToInt32(request[(byte)Key2012.WorldObjectType]);
        return new WorldObject
        {
            Id = id,
            ParentId = Convert.ToInt32(request[(byte)Key2012.WorldObjectGroupID]),
            Type = (WorldObjectType)(types.Map(type) ?? type),
            Position = Floats(request, Key2012.PosX, Key2012.PosY, Key2012.PosZ),
            Rotation = Floats(request, Key2012.RotX, Key2012.RotY, Key2012.RotZ, Key2012.RotW),
            Scale = Floats(request, Key2012.ScaleX, Key2012.ScaleY, Key2012.ScaleZ),
            Data = Packed(request[(byte)Key2012.WorldObjectData]),
            Runtime = Packed(request[(byte)Key2012.WorldObjectRunTimeData]),
        };
    }

    public static List<(string Key, PackedType Type, object Value)> Packed(object? raw) =>
        raw is null ? [] : PackedData.FromPhoton((Dictionary<object, object?>)PhotonValues.Normalize(raw)!);

    public static Dictionary<object, object?> Table(List<(string Key, PackedType Type, object Value)> values) =>
        (Dictionary<object, object?>)PackedData.ToPhoton(values);

    static float[] Floats(OperationRequest request, params Key2012[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
