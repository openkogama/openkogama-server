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
        Register(new GetBuiltInItemBusinessData());
        Register(new GetDBTimeTicks());
        Register(new RequestMaterials(session));
        Register(new DBQuery());
        Register(new LargeDBQuery());
        Register(new GetNextResultSet(session));
        Register(new RequestStreamingAssetList());
        Register(new RequestStreamingAssetInventory());
        Register(new PurchaseProduct());
        Register(new SetAvatarAccessorySlot(session));
        Register(new UpdateAvatarAccessoryOffset(session));
        Register(new InitializeAvatarEdit(session));
        Register(new GetActiveAvatar(session));
        Register(new CreateGameSnapshot(session));
        Register(new GetNextGameBatch(session));
        Register(new RequestFriends());
        Register(new SetTeam(session));
        Register(new LevelChanged(session));
        Register(new ReportCaptureFlag(session));
        Register(new SetActorReady());
        Register(new UpdateWorldObject(session));
        Register(new UpdatePrototype(session));
        Register(new RequestBuiltInItem(session));
        Register(new AddItemToWorld(session));
        Register(new AddWorldObjectToInventory(session));
        Register(new RemoveItemFromInventory(session));
        Register(new UpdateInventorySlots(session));
        Register(new TransferOwnership(session));
        Register(new UnregisterWorldObject(session));
        Register(new CloneWorldObjectTree(session));
        Register(new RequestWoUniquePrototype(session));
        Register(new TriggerBox(session, entering: true));
        Register(new TriggerBox(session, entering: false));
        Register(new WorldObjectRPC(session));
        Register(new SpawnVehicleWithDriver(server, session));
        Register(new AttachWorldObjectToSeat(session));
        Register(new DetachWorldObjectFromVehicle(session));
        Register(new LinkOperation(session, objectLink: false, adding: true));
        Register(new LinkOperation(session, objectLink: false, adding: false));
        Register(new LinkOperation(session, objectLink: true, adding: true));
        Register(new LinkOperation(session, objectLink: true, adding: false));
        Register(new ResetTerrain(session));
        Register(new RuntimeEvent(session));
        Register(new ResetLogicChunk(session));
        Register(new WorldObjectData(session, DataChange.Replace));
        Register(new WorldObjectData(session, DataChange.Merge));
        Register(new WorldObjectData(session, DataChange.Remove));
        Register(new UpdateWorldObjectRunTimeData(session));
        Register(new UpdateLineOfFire(server));
        Register(new PostGameMsg(session));
    }

    void Register(IOperationHandler handler) => _handlers[handler.Code] = handler;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (!_handlers.TryGetValue(request.OperationCode, out IOperationHandler? handler))
        {
            _log?.Invoke($"peer {peer.Id}: no handler for {request}");
            return;
        }

        try
        {
            handler.Handle(peer, request);
        }
        catch (Exception error)
        {
            _log?.Invoke($"peer {peer.Id}: op {request.OperationCode} failed: {error}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
        }
    }
}
