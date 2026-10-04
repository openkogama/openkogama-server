using System.Numerics;
using OpenKogama.Api;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class PluginObject(WorldObject obj) : IWorldObject
{
    public int Id => obj.Id;
    public int ParentId => obj.ParentId;
    public string Type => obj.Type.ToString();
    public Vector3 Position => new(obj.Position[0], obj.Position[1], obj.Position[2]);
    public Quaternion Rotation => new(obj.Rotation[0], obj.Rotation[1], obj.Rotation[2], obj.Rotation[3]);
    public Vector3 Scale => new(obj.Scale[0], obj.Scale[1], obj.Scale[2]);
    public IReadOnlyDictionary<string, object> Data => Values(obj.Data);

    static Dictionary<string, object> Values(List<(string Key, PackedType Type, object Value)> data) =>
        data.ToDictionary(pair => pair.Key, pair => pair.Value is List<(string, PackedType, object)> nested ? Values(nested) : pair.Value);
}
