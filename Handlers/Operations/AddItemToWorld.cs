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

        Snapshot template = WorldSerializer.Read(item.Bytes, runtime: false);
        WorldObject root = template.Objects.First(obj => obj.ParentId == -1);
        root.ItemId = itemId;
        root.Position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ);
        root.Rotation = Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW);
        if (request.Parameters.ContainsKey((byte)ParameterKey.ScaleX))
            root.Scale = Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ);
        root.Owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);

        int parentId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectGroupID]);
        Snapshot added = session.World.Insert(template, parentId);
        session.World.MarkChanged();
        session.Teams.Update();
        if (root.Type == WorldObjectType.RoundCube) session.Round.Start();

        GetNextGameBatch.SendAdded(session, peer.Id, added);

        Console.WriteLine($"peer {peer.Id}: placed {item.Name} as {added.Objects[0].Id}");
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
