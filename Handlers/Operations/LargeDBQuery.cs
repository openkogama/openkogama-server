using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class LargeDBQuery : IOperationHandler
{
    public byte Code => (byte)OperationCode.LargeDBQuery;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var type = (DBQueryType)Convert.ToByte(request[(byte)ParameterKey.DBQuery]);

        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.LargeDBQueryID] = (int)type },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: large dbquery {type}");
    }
}
