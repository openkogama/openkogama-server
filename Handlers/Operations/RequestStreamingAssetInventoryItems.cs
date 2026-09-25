using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestStreamingAssetInventoryItems : IOperationHandler
{
    static readonly long Owned = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public byte Code => (byte)OperationCode.RequestStreamingAssetInventoryItems;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int[] ids = request[(byte)ParameterKey.StreamingAssetInventoryIDs] as int[] ?? [];
        PhotonDictionary items = PhotonDictionary.Untyped();

        foreach (int id in ids)
        {
            if (StreamingAssets.Find(id) is not { } asset) continue;

            PhotonDictionary entry = asset.Describe();
            entry.Add((byte)DBQueryKey.StreamingAssetID, asset.Id);
            entry.Add((byte)DBQueryKey.IsRented, false);
            entry.Add((byte)DBQueryKey.PurchaseTimeTicks, Owned);
            items.Add(id, entry);
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.StreamingAssetInventoryIDs] = ids,
                [(byte)ParameterKey.StreamingAssetInventoryItems] = items,
            },
        });
    }
}
