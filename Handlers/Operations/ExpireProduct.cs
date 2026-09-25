using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class ExpireProduct : IOperationHandler
{
    public byte Code => (byte)OperationCode.ExpireProduct;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ProductTypeID] = Convert.ToInt32(request[(byte)ParameterKey.ProductTypeID]),
                [(byte)ParameterKey.ProductInventoryID] = Convert.ToInt32(request[(byte)ParameterKey.ProductInventoryID]),
            },
        });
    }
}
