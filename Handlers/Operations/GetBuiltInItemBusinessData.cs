using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetBuiltInItemBusinessData : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetBuiltInItemBusinessData;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.ItemBusinessData] = PhotonDictionary.Untyped() },
        };

        peer.Send(response);
    }
}
