using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetList : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestStreamingAssetList;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.StreamingAssetList] = PhotonDictionary.Untyped() },
        };

        peer.Send(response);
    }
}
