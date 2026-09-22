using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class DBQuery : IOperationHandler
{
    public byte Code => (byte)OperationCode.DBQuery;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var type = (DBQueryType)Convert.ToByte(request[(byte)ParameterKey.DBQuery]);
        PhotonDictionary outData = PhotonDictionary.Untyped();

        switch (type)
        {
            case DBQueryType.RequestItemCategories:
                outData.Add(1, "Cube");
                outData.Add(2, "Model");
                outData.Add(3, "Avatar");
                break;

            case DBQueryType.RequestPlanetOwnershipTypes:
                outData.Add(0, "Owner");
                outData.Add(1, "Visitor");
                break;
        }

        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.DBQueryOutData] = outData },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: dbquery {type}");
    }
}
