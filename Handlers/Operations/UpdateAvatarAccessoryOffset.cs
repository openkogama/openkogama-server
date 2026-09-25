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

        var moved = new List<(string Id, object Entry)>();
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
                moved.Add((inventoryId, PackedData.ToPhoton(accessory)));
            }
        });

        if (session.For(peer) is Player owner)
            Stores.Profiles.SetAccessoryOffset(session.AvatarOfBody(bodyId, owner.ProfileId), slot, offset);

        if (moved.Count == 0) return;

        PhotonDictionary removed = PhotonDictionary.Untyped();
        PhotonDictionary added = PhotonDictionary.Untyped();
        foreach ((string id, object entry) in moved)
        {
            removed.Add(id, "");
            added.Add(id, entry);
        }

        var remove = new EventData((byte)EventCode.RemoveWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = bodyId,
                [(byte)ParameterKey.WorldObjectDataToRemove] = Accessories(removed),
            },
        };
        var add = new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = bodyId,
                [(byte)ParameterKey.WorldObjectData] = Accessories(added),
            },
        };
        foreach (Player player in session.Players)
        {
            if (player.Peer == peer) continue;
            player.Peer.Send(remove);
            player.Peer.Send(add);
        }
    }

    static PhotonDictionary Accessories(PhotonDictionary accessories)
    {
        PhotonDictionary blueprint = PhotonDictionary.Untyped();
        blueprint.Add("3", accessories);
        PhotonDictionary data = PhotonDictionary.Untyped();
        data.Add("BlueprintData", blueprint);
        return data;
    }

    static List<(string Key, PackedType Type, object Value)>? Find(List<(string Key, PackedType Type, object Value)> pairs, string key) =>
        pairs.FirstOrDefault(pair => pair.Key == key).Value as List<(string Key, PackedType Type, object Value)>;
}
