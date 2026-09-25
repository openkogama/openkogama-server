using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RentProduct : IOperationHandler
{
    public byte Code => (byte)OperationCode.RentProduct;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var product = request.Parameters.GetValueOrDefault((byte)ParameterKey.RentProductData) as PhotonDictionary;
        object? id = product?.Entries.FirstOrDefault(entry => entry.Key is byte or short or int && Convert.ToInt32(entry.Key) == 105).Value;

        if (id is null || StreamingAssets.Find(Convert.ToInt32(id)) is not { } asset)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        PhotonDictionary result = PhotonDictionary.Untyped();
        result.Add((byte)DBQueryKey.StreamingAssetInventoryID, asset.Id);
        result.Add((byte)DBQueryKey.PurchaseTimeTicks, DateTime.Now.Ticks);
        result.Add((byte)DBQueryKey.RentExpireSeconds, asset.RentSeconds);

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.RentProductData] = result },
        });
    }
}
