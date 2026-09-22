using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetInventory : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestStreamingAssetInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.StreamingAssetInventory] = PhotonDictionary.Untyped() },
        };

        peer.Send(response);
    }
}
