using OpenKogama.Content;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class SoundEmitters
{
    const string Url = "url";
    const string Id = "id";
    const string Name = "name";

    public static WorldObject For(Player viewer, WorldObject obj)
    {
        if (!viewer.Legacy || obj.Type != WorldObjectType.SoundEmitter || obj.Data.Find(pair => pair.Key == Url).Value is not string url
            || obj.Data.Exists(pair => pair.Key == Id) && obj.Data.Exists(pair => pair.Key == Name))
            return obj;

        StreamingAsset? asset = viewer.Streaming.Assets.Find(asset => asset.Path == url);
        string name = asset?.Name ?? CustomContent.Sounds.All.FirstOrDefault(sound => sound.Url == url)?.Name ?? Path.GetFileNameWithoutExtension(url);
        return new WorldObject
        {
            Id = obj.Id,
            ParentId = obj.ParentId,
            ItemId = obj.ItemId,
            Type = obj.Type,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
            Data = [.. obj.Data.Where(pair => pair.Key is not (Id or Name)), (Id, PackedType.Int32, asset?.Id ?? 0), (Name, PackedType.String, name)],
            Owner = obj.Owner,
            PreviewOwner = obj.PreviewOwner,
            Runtime = obj.Runtime,
            Transient = obj.Transient,
        };
    }
}
