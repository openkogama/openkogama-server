using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddItemToWorld(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.AddItemToWorld;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)ParameterKey.ItemID]);
        Player? owner = session.For(peer);
        Item? item = Inventories.Find(owner?.ProfileId ?? 0, itemId, owner?.ClientVersion ?? Kogama.Protocols.ClientProtocols.ServerVersion);
        if (item is null)
        {
            Console.WriteLine($"peer {peer.Id}: unknown item {itemId}");
            return;
        }

        float[]? scale = request.Parameters.ContainsKey((byte)ParameterKey.ScaleX)
            ? Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ)
            : null;
        WorldObject? placed = Place(session.World, session, item,
            Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ),
            Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW),
            scale, Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]),
            Convert.ToInt32(request[(byte)ParameterKey.WorldObjectGroupID]), owner?.Actor ?? peer.Id);
        if (placed is not null) Plugins.PluginHost.Added(session, placed, owner);

        Console.WriteLine($"peer {peer.Id}: placed {item.Name} as {placed?.Id}");
    }

    public static WorldObject? Place(GameWorld world, Session? session, Item item, float[] position, float[] rotation, float[]? scale, int owner, int parentId, int actor)
    {
        Snapshot template = WorldSerializer.Read(item.Bytes, runtime: false);
        WorldObject root = template.Objects.First(obj => obj.ParentId == -1);
        root.ItemId = item.Id;
        root.Position = position;
        root.Rotation = rotation;
        if (scale is not null) root.Scale = scale;
        root.Owner = owner;

        Snapshot added = world.Insert(template, parentId);
        if (added.Objects.Any(obj => obj.Type == WorldObjectType.AvatarSpawnRoleCreator))
        {
            AvatarClasses.AddPreviews(world);
            added = world.SubtreeSnapshot(added.Objects[0].Id);
        }
        world.MarkChanged();
        if (session is not null)
        {
            session.Teams.Update();
            if (root.Type == WorldObjectType.RoundCube && session.Play) session.Round.Start();
            GetNextGameBatch.SendAdded(session, actor, added);
        }
        return world.Find(added.Objects[0].Id);
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
