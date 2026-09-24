using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetList : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestStreamingAssetList;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary list = PhotonDictionary.Untyped();
        request.Parameters.TryGetValue((byte)ParameterKey.StreamingAssetTypeIDs, out object? types);
        foreach (StreamingAsset asset in StreamingAssets.For("2015").OfTypes(types))
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.StreamingAssetTypeID, asset.Type);
            entry.Add((byte)DBQueryKey.StreamingAssetCategoryID, asset.Category);
            entry.Add((byte)DBQueryKey.StreamingAssetName, asset.Name);
            entry.Add((byte)DBQueryKey.StreamingAssetDescription, asset.Description);
            entry.Add((byte)DBQueryKey.StreamingAssetURL, asset.Path);
            entry.Add((byte)DBQueryKey.PriceGold, 0);
            entry.Add((byte)DBQueryKey.PriceSilver, 0);
            entry.Add((byte)DBQueryKey.RentPriceSilver, 0);
            entry.Add((byte)DBQueryKey.RentPriceGold, 0);
            entry.Add((byte)DBQueryKey.RentExpireSeconds, 0);
            list.Add(asset.Id, entry);
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.StreamingAssetList] = list },
        });
    }
}
