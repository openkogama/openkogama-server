using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetDBTimeTicks : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetDBTimeTicks;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.DBTimeTicks] = DateTime.UtcNow.Ticks,
            },
        };

        peer.Send(response);
    }
}
