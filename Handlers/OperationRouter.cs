using OpenKogama.Photon;
using OpenKogama.Handlers.Operations;

namespace OpenKogama.Handlers;

public sealed class OperationRouter
{
    readonly Dictionary<byte, IOperationHandler> _handlers = [];
    readonly Action<string>? _log;

    public OperationRouter(Action<string>? log = null)
    {
        _log = log;
        Register(new Join());
        Register(new GetCreditStatus());
        Register(new GetDBTimeTicks());
    }

    void Register(IOperationHandler handler) => _handlers[handler.Code] = handler;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (_handlers.TryGetValue(request.OperationCode, out IOperationHandler? handler))
            handler.Handle(peer, request);
        else
            _log?.Invoke($"peer {peer.Id}: no handler for {request}");
    }
}
