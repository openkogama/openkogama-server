using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Plugins;

sealed class PluginWorld(Session session) : IWorld
{
    readonly WorldEditor _editor = new(session.World, session);

    public int Id => session.WorldId ?? 0;
    public string Name => session.Name;
    public bool Play => session.Play;
    public IReadOnlyList<IPlayer> Players => [.. session.Players.Select(player => PluginHost.PlayerOf(session, player))];
    public IReadOnlyList<INpc> Npcs => [.. session.Npcs.Select(npc => new PluginNpc(session, npc))];
    public IReadOnlyList<IWorldObject> Objects => _editor.Objects;
    public ITerrain Terrain => _editor.Terrain;

    public IWorldObject? Find(int id) => _editor.Find(id);

    public IWorldObject Add(string type, Vector3 position, IReadOnlyDictionary<string, object>? data = null, Quaternion? rotation = null, Vector3? scale = null, int? parentId = null) =>
        _editor.Add(type, position, data, rotation, scale, parentId);

    public bool Remove(int id) => _editor.Remove(id);

    public bool Move(int id, Vector3 position, Quaternion? rotation = null) => _editor.Move(id, position, rotation);

    public bool SetData(int id, string key, object value) => _editor.SetData(id, key, value);

    public bool SetData(int id, IReadOnlyDictionary<string, object> values) => _editor.SetData(id, values);

    public INpc SpawnNpc(string name, Vector3 position, float yaw = 0, IPlayer? skinOf = null) =>
        SpawnNpc(name, position, yaw, skinOf?.Skin);

    public INpc SpawnNpc(string name, Vector3 position, float yaw, AvatarSkin? skin, float size = 1f, int level = 1)
    {
        List<AvatarPart>? parts = skin?.Parts.Select(part => new AvatarPart { Bone = part.Bone, Scale = part.Scale, Cubes = part.Cubes }).ToList();
        List<WornAccessory>? accessories = skin?.Accessories.Select(worn => new WornAccessory(worn.Item, worn.Slot, worn.Offset, worn.Scale)).ToList();
        return new PluginNpc(session, session.AddNpc(name, [position.X, position.Y, position.Z], yaw, parts, accessories, size, level));
    }

    public void Announce(string text, string? icon = null) => session.Announce(text, icon, session.Players);

    public void Notify(string text, bool brief = false)
    {
        foreach (Player player in session.Players)
            ServerChat.Notify(player, text, brief);
    }

    public void Broadcast(string text, string? color = null)
    {
        foreach (Player player in session.Players)
            ServerChat.Message(player, text, color);
    }
}
