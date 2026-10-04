using System.Numerics;

namespace OpenKogama.Api;

public interface IWorldObject
{
    int Id { get; }
    int ParentId { get; }
    string Type { get; }
    Vector3 Position { get; }
    Quaternion Rotation { get; }
    Vector3 Scale { get; }
    IReadOnlyDictionary<string, object> Data { get; }
}
