using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetNextResultSet : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetNextResultSet;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int queryId = Convert.ToInt32(request[(byte)ParameterKey.LargeDBQueryID]);

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.DBQueryOutData] = PhotonDictionary.Untyped(),
                [(byte)ParameterKey.LargeDBQueryID] = queryId,
                [(byte)ParameterKey.HasMoreResultSets] = false,
            },
        };

        peer.Send(response);
    }
}
