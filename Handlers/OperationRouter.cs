using OpenKogama.Game;
using OpenKogama.Photon;
using OpenKogama.Handlers.Operations;

namespace OpenKogama.Handlers;

public sealed class OperationRouter
{
    readonly Dictionary<byte, IOperationHandler> _handlers = [];
    readonly Action<string>? _log;

    public OperationRouter(PhotonServer server, Session session, Action<string>? log = null)
    {
        _log = log;
        Register(new Join(session));
        Register(new GetCreditStatus());
        Register(new GetDBTimeTicks());
        Register(new RequestMaterials());
        Register(new DBQuery());
        Register(new RequestStreamingAssetList());
        Register(new RequestStreamingAssetInventory());
        Register(new CreateGameSnapshot(session));
        Register(new GetNextGameBatch(session));
        Register(new RequestFriends());
        Register(new SetTeam());
        Register(new SetActorReady());
        Register(new UpdateWorldObject(server));
        Register(new UpdateWorldObjectRunTimeData(server));
        Register(new UpdateLineOfFire(server));
        Register(new PostGameMsg(session));
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
