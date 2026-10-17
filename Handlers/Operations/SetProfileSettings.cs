using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetProfileSettings(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetProfileSettings;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player
            && request[(byte)ParameterKey.Id] is not null
            && request[(byte)ParameterKey.ItemTypeID] is not null)
            ProfileSettings.Set(player.ProfileId,
                Convert.ToInt32(request[(byte)ParameterKey.Id]),
                Convert.ToInt32(request[(byte)ParameterKey.ItemTypeID]),
                request[(byte)ParameterKey.SettingValue]);
        peer.Send(new OperationResponse(request));
    }
}
