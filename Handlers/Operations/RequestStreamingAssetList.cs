using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetList(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestStreamingAssetList;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary list = PhotonDictionary.Untyped();
        request.Parameters.TryGetValue((byte)ParameterKey.StreamingAssetTypeIDs, out object? types);
        foreach (StreamingAsset asset in ClientContent.Streaming(peer, session.For(peer)?.Build).OfTypes(types))
        {
            list.Add(asset.Id, asset.Describe());
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.StreamingAssetList] = list },
        });
    }
}
