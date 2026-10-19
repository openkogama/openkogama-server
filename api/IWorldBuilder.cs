using System.Numerics;

namespace OpenKogama.Api;

public interface IWorldBuilder
{
    IReadOnlyList<IWorldObject> Objects { get; }
    ITerrain Terrain { get; }
    IWorldObject? Find(int id);
    IReadOnlyList<IWorldObject> Markers(string name);
    IWorldObject Add(string type, Vector3 position, IReadOnlyDictionary<string, object>? data = null, Quaternion? rotation = null, Vector3? scale = null, int? parentId = null);
    bool Remove(int id);
    bool Link(int from, int to);
    bool Move(int id, Vector3 position, Quaternion? rotation = null, float seconds = 0);
    bool SetData(int id, string key, object value);
    bool SetData(int id, IReadOnlyDictionary<string, object> values);
    bool SetSound(int id, ISound sound);
}
