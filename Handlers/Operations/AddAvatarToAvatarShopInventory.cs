using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddAvatarToAvatarShopInventory(Session session) : IOperationHandler
{
    const int ListingImage = 3;

    public byte Code => (byte)OperationCode.AddAvatarToAvatarShopInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (session.For(peer) is not Player player || session.World.Find(bodyId) is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Snapshot copy = WorldSerializer.Read(WorldSerializer.Write(session.World.SubtreeSnapshot(bodyId), runtime: false), runtime: false);
        copy.Objects.First(obj => obj.Id == bodyId).ParentId = -1;
        byte[] data = WorldSerializer.Write(copy, runtime: false);

        int avatar = session.AvatarOfBody(bodyId, player.ProfileId);
        string name = request[(byte)ParameterKey.AvatarName] as string is { Length: > 0 } given ? given : $"Avatar {avatar}";
        int price = Math.Max(0, Convert.ToInt32(request[(byte)ParameterKey.SilverAmount]));
        int listing = Stores.Market.Put(ListingKind.Avatar, player.ProfileId, avatar, name, "", 0, price, data);

        if (request[(byte)ParameterKey.ImageData] is byte[] { Length: > 0 } image)
            Stores.Images.SaveImage(ListingImage, listing, image);

        peer.Send(new OperationResponse(request));
        Console.WriteLine($"profile {player.ProfileId}: listed avatar {avatar} as {listing} for {price} silver");
    }
}
