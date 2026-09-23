namespace OpenKogama.World;

public sealed class WorldObject
{
    public int Id { get; set; }
    public int ParentId { get; set; } = -1;
    public int ItemId { get; set; }
    public WorldObjectType Type { get; set; }

    public float[] Position { get; set; } = [0f, 0f, 0f];
    public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];
    public float[] Scale { get; set; } = [1f, 1f, 1f];

    public List<(string Key, PackedType Type, object Value)> Data { get; set; } = [];
    public int? Owner { get; set; }
    public int? PreviewOwner { get; set; }
    public List<(string Key, PackedType Type, object Value)> Runtime { get; set; } = [];
    public bool Transient { get; set; }

    public void SetRuntime(string key, PackedType type, object value)
    {
        Runtime.RemoveAll(pair => pair.Key == key);
        Runtime.Add((key, type, value));
    }

    public int? PrototypeId =>
        Data.FirstOrDefault(pair => pair.Key == "protoTypeID").Value as int?;
}
