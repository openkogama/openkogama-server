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
        Item? item = Inventories.Find(session.For(peer)?.ProfileId ?? 0, itemId);
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
        root.Scale = Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ);
        root.Owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);

        int parentId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectGroupID]);
        Snapshot added = session.World.Insert(template, parentId);
        session.World.MarkChanged();
        session.Teams.Update();
        if (root.Type == WorldObjectType.RoundCube) session.Round.Start();

        var evt = new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.Data] = WorldSerializer.Write(added),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);

        Console.WriteLine($"peer {peer.Id}: placed {item.Name} as {added.Objects[0].Id}");
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
