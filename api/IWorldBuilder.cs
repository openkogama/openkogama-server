using System.Numerics;

namespace OpenKogama.Api;

public interface IWorldBuilder
{
    IReadOnlyList<IWorldObject> Objects { get; }
    IReadOnlyList<WorldLink> Links { get; }
    IReadOnlyList<WorldLink> ObjectLinks { get; }
    ITerrain Terrain { get; }
    ICubeModel? Model(int objectId);
    IWorldObject? Find(int id);
    IReadOnlyList<IWorldObject> Markers(string name);
    IWorldObject Add(string type, Vector3 position, IReadOnlyDictionary<string, object>? data = null, Quaternion? rotation = null, Vector3? scale = null, int? parentId = null);
    IWorldObject? AddItem(string name, Vector3 position, Quaternion? rotation = null, int? parentId = null);
    bool Remove(int id);
    bool Link(int from, int to);
    bool Unlink(int from, int to);
    bool LinkObjects(int from, int to);
    bool UnlinkObjects(int from, int to);
    bool Move(int id, Vector3 position, Quaternion? rotation) => Move(id, position, rotation, 0);
    bool Move(int id, Vector3 position, Quaternion? rotation = null, float seconds = 0);
    bool SetData(int id, string key, object value);
    bool SetData(int id, IReadOnlyDictionary<string, object> values);
    bool SetSound(int id, ISound sound);
}
