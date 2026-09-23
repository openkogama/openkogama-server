using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetBuiltInItemBusinessData : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetBuiltInItemBusinessData;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary items = PhotonDictionary.Untyped();
        items.Add(1, Entry(category: 1, name: "CubeModel", resellable: true));
        items.Add(2, Entry(category: 9, name: "Group", resellable: false));

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.ItemBusinessData] = items },
        });
    }

    static PhotonDictionary Entry(int category, string name, bool resellable)
    {
        PhotonDictionary entry = PhotonDictionary.Untyped();
        entry.Add((byte)DBQueryKey.ItemCategoryID, category);
        entry.Add((byte)DBQueryKey.ItemTypeID, 0);
        entry.Add((byte)DBQueryKey.ItemName, name);
        entry.Add((byte)DBQueryKey.Resellable, resellable);
        return entry;
    }
}
