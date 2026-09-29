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
        Player? owner = session.For(peer);
        int slot = Convert.ToInt32(request[(byte)ParameterKey.AvatarAccessorySlot]);
        if (owner?.ModernAccessories == true) slot = Accessories.ToLegacySlot(slot);
        float offset = Convert.ToSingle(request[(byte)ParameterKey.AvatarAccessoryOffset]);

        Accessories.Change(session, bodyId, worn =>
        {
            foreach ((_, _, object value) in worn)
                if (value is List<(string Key, PackedType Type, object Value)> entry && Accessories.SlotOf(entry) == slot)
                    Accessories.Set(entry, "3", PackedType.Single, offset);
        });

        if (owner is not null)
            Stores.Profiles.SetAccessoryOffset(session.AvatarOfBody(bodyId, owner.ProfileId), slot, offset);
    }
}
