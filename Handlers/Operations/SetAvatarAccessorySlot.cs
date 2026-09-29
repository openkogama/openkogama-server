using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class SetAvatarAccessorySlot(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetAvatarAccessorySlot;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        Player? owner = session.For(peer);
        bool modern = owner?.ModernAccessories == true;
        int assetId = Convert.ToInt32(request[(byte)(modern ? ParameterKey.StreamingAssetID : ParameterKey.StreamingAssetInventoryID)]);
        float offset = request[(byte)ParameterKey.AvatarAccessoryOffset] is { } rawOffset ? Convert.ToSingle(rawOffset) : 0f;
        float scale = request[(byte)ParameterKey.Scale] is { } rawScale ? Convert.ToSingle(rawScale) : 1f;

        StreamingAsset? asset = ClientContent.Streaming(peer).Assets.Find(asset => asset.Id == assetId);
        if (asset is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        int slot = modern ? Accessories.ToLegacySlot(Accessories.ModernSlotOf(asset)) : Convert.ToInt32(request[(byte)ParameterKey.AvatarAccessorySlot]);
        Accessories.Change(session, bodyId, worn =>
        {
            worn.RemoveAll(pair => pair.Key == asset.Id.ToString()
                || modern && pair.Value is List<(string Key, PackedType Type, object Value)> entry && Accessories.SlotOf(entry) == slot);
            if (slot != 0) worn.Add(Accessories.Entry(asset, slot, offset, scale));
        });

        if (owner is not null)
        {
            int avatar = session.AvatarOfBody(bodyId, owner.ProfileId);
            if (modern) Stores.Profiles.ClearAccessorySlot(avatar, slot);
            Stores.Profiles.SetAccessory(avatar, asset.Id, slot, offset, scale);
        }

        peer.Send(new OperationResponse(request));
    }
}
