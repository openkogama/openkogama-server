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
        int avatarId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        int inventoryId = Convert.ToInt32(request[(byte)ParameterKey.StreamingAssetInventoryID]);
        int slot = Convert.ToInt32(request[(byte)ParameterKey.AvatarAccessorySlot]);
        float offset = Convert.ToSingle(request[(byte)ParameterKey.AvatarAccessoryOffset]);

        StreamingAsset? asset = StreamingAssets.For("2015").Assets.Find(asset => asset.Id == inventoryId);
        if (asset is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        bool equip = slot != 0;
        PhotonDictionary accessory = PhotonDictionary.Untyped();
        if (equip)
        {
            accessory.Add("1", inventoryId);
            accessory.Add("2", slot);
            accessory.Add("3", offset);
            accessory.Add("4", asset.Path);
            accessory.Add("5", DateTime.Now.Ticks);
            accessory.Add("6", 0);
        }

        PhotonDictionary accessories = PhotonDictionary.Untyped();
        accessories.Add(inventoryId.ToString(), equip ? accessory : "");
        PhotonDictionary blueprint = PhotonDictionary.Untyped();
        blueprint.Add("3", accessories);
        PhotonDictionary change = PhotonDictionary.Untyped();
        change.Add("BlueprintData", blueprint);

        var table = (Dictionary<object, object?>)PhotonValues.Normalize(change)!;
        session.World.Modify(avatarId, obj =>
        {
            if (equip) PackedData.Merge(obj.Data, table);
            else PackedData.Remove(obj.Data, table);
        });

        if (session.For(peer) is Player owner)
            Stores.Profiles.SetAccessory(session.AvatarOfBody(avatarId, owner.ProfileId), inventoryId, slot, offset);

        peer.Send(new OperationResponse(request));

        var evt = new EventData((byte)(equip ? EventCode.UpdateWorldObjectDataPartial : EventCode.RemoveWorldObjectDataPartial))
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = avatarId,
                [(byte)(equip ? ParameterKey.WorldObjectData : ParameterKey.WorldObjectDataToRemove)] = change,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
