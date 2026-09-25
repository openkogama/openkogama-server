using OpenKogama.Game;
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
                foreach ((int id, string name) in Items.For("2015").Categories)
                    outData.Add(id, name);
                break;

            case DBQueryType.RequestPlanetOwnershipTypes:
                outData.Add((int)PlanetOwnership.None, "None");
                outData.Add((int)PlanetOwnership.Editor, "Editor");
                outData.Add((int)PlanetOwnership.Owner, "Owner");
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
