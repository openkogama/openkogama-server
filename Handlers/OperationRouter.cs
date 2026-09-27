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
        Register(new Leave());
        Register(new GetCreditStatus());
        Register(new GetBuiltInItemBusinessData());
        Register(new GetDBTimeTicks());
        Register(new RequestMaterials(session));
        Register(new DBQuery());
        Register(new DBQuery(Kogama.OperationCode.GetItemCategories, Kogama.DBQueryType.RequestItemCategories));
        Register(new DBQuery(Kogama.OperationCode.GetPlanetOwnershipTypes, Kogama.DBQueryType.RequestPlanetOwnershipTypes));
        Register(new LargeDBQuery());
        Register(new LargeDBQuery(Kogama.OperationCode.LargeDBQueryInventory));
        Register(new LargeDBQuery(Kogama.OperationCode.LargeDBQueryAvatarShopInventory));
        Register(new GetNextResultSet(session));
        Register(new RequestStreamingAssetList());
        Register(new RequestStreamingAssetInventory());
        Register(new PurchaseProduct(session));
        Register(new RentProduct());
        Register(new ExpireProduct());
        Register(new RequestStreamingAssetInventoryItems());
        Register(new SetAvatarAccessorySlot(session));
        Register(new UpdateAvatarAccessoryOffset(session));
        Register(new InitializeAvatarEdit(session));
        Register(new GetActiveAvatar(session));
        Register(new SetActiveAvatar(session));
        Register(new ResetAvatar(session));
        Register(new CreateGameSnapshot(session));
        var batch = new GetNextGameBatch(session);
        Register(batch);
        Register(new Syncronize(session, this, batch));
        Register(new RequestFriends(session));
        Register(new RequestFriendship(session, byName: true));
        Register(new RequestFriendship(session, byName: false));
        Register(new AnswerFriendship(session, accept: true));
        Register(new AnswerFriendship(session, accept: false));
        Register(new SetTeam(session));
        Register(new LevelChanged(session));
        Register(new ReportCaptureFlag(session));
        Register(new SetActorReady(session));
        Register(new RegisterWorldObject(session));
        Register(new AddPlanetToPlanet(session));
        Register(new UpdateWorldObject(session));
        Register(new UpdatePrototype(session));
        Register(new UpdatePrototypeScale(session));
        Register(new Ungroup(session));
        Register(new LockHierarchy(session));
        Register(new UploadScreenshot(session));
        Register(new PublishPlanet(session));
        Register(new ClientLog());
        Register(new UpdateNetworkInput(session));
        Register(new XPRewarded(session));
        Register(new GameCoinBooster());
        Register(new TransferWorldObjectsToGroup(session));
        Register(new RequestBuiltInItem(session));
        Register(new AddItemToWorld(session));
        Register(new AddWorldObjectToInventory(session));
        Register(new PurchaseItem(session));
        Register(new AddWorldObjectToInventoryDev(session));
        Register(new Ban(session));
        Register(new GetMarketPlaceItem(session));
        Register(new AddItemToMarketPlace(session));
        Register(new RemoveItemFromMarketPlace(session));
        Register(new AddAvatarToAvatarShopInventory(session));
        Register(new DeleteAvatarFromShopInventory(session));
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
        Register(new UpdateLineOfFire(session));
        Register(new PostGameMsg(session));
    }

    public IEnumerable<byte> Codes => _handlers.Keys;

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
