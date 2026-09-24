using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class PurchaseProduct : IOperationHandler
{
    public byte Code => (byte)OperationCode.PurchaseProduct;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (!request.Parameters.TryGetValue((byte)ParameterKey.PurchaseProductData, out object? data)
            || data is not PhotonDictionary product
            || !product.Entries.TryGetValue(105, out object? id))
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        PhotonDictionary result = PhotonDictionary.Untyped();
        result.Add((byte)DBQueryKey.StreamingAssetInventoryID, id);
        result.Add((byte)DBQueryKey.PurchaseTimeTicks, DateTime.Now.Ticks);

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.PurchaseProductData] = result },
        });
    }
}
