using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateAvatarAccessoryOffset(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateAvatarAccessoryOffset;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        int slot = Convert.ToInt32(request[(byte)ParameterKey.AvatarAccessorySlot]);
        float offset = Convert.ToSingle(request[(byte)ParameterKey.AvatarAccessoryOffset]);

        var moved = new List<string>();
        session.World.Modify(bodyId, body =>
        {
            if (Find(body.Data, "BlueprintData") is not { } blueprint || Find(blueprint, "3") is not { } accessories) return;

            foreach ((string inventoryId, _, object value) in accessories)
            {
                if (value is not List<(string Key, PackedType Type, object Value)> accessory) continue;
                int index = accessory.FindIndex(pair => pair.Key == "2");
                if (index < 0 || Convert.ToInt32(accessory[index].Value) != slot) continue;

                accessory.RemoveAll(pair => pair.Key == "3");
                accessory.Add(("3", PackedType.Single, offset));
                moved.Add(inventoryId);
            }
        });

        if (session.For(peer) is Player owner)
            Stores.Profiles.SetAccessoryOffset(owner.ProfileId, slot, offset);

        if (moved.Count == 0) return;

        PhotonDictionary changes = PhotonDictionary.Untyped();
        foreach (string inventoryId in moved)
        {
            PhotonDictionary accessory = PhotonDictionary.Untyped();
            accessory.Add("3", offset);
            changes.Add(inventoryId, accessory);
        }
        PhotonDictionary blueprint = PhotonDictionary.Untyped();
        blueprint.Add("3", changes);
        PhotonDictionary data = PhotonDictionary.Untyped();
        data.Add("BlueprintData", blueprint);

        var evt = new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = bodyId,
                [(byte)ParameterKey.WorldObjectData] = data,
            },
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer) player.Peer.Send(evt);
    }

    static List<(string Key, PackedType Type, object Value)>? Find(List<(string Key, PackedType Type, object Value)> pairs, string key) =>
        pairs.FirstOrDefault(pair => pair.Key == key).Value as List<(string Key, PackedType Type, object Value)>;
}
