using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestFriends : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestFriends;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.Friends] = PhotonDictionary.Untyped() },
        };

        peer.Send(response);
    }
}
