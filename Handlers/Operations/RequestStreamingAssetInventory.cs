using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetInventory : IOperationHandler
{
    static readonly long Owned = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public byte Code => (byte)OperationCode.RequestStreamingAssetInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary inventory = PhotonDictionary.Untyped();
        request.Parameters.TryGetValue((byte)ParameterKey.StreamingAssetTypeIDs, out object? types);
        foreach (StreamingAsset asset in StreamingAssets.For("2015").OfTypes(types))
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.StreamingAssetID, asset.Id);
            entry.Add((byte)DBQueryKey.IsRented, false);
            entry.Add((byte)DBQueryKey.PurchaseTimeTicks, Owned);
            inventory.Add(asset.Id, entry);
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.StreamingAssetInventory] = inventory },
        });
    }
}
