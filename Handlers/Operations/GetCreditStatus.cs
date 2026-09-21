using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetCreditStatus : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetCreditStatus;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.GoldAmount] = 0,
                [(byte)ParameterKey.SilverAmount] = 0,
            },
        };

        peer.Send(response);
    }
}
