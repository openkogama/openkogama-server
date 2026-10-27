using OpenKogama.Game;
using OpenKogama.Photon;
using OpenKogama.Handlers.Operations;

namespace OpenKogama.Handlers;

public sealed class OperationRouter
{
    readonly Dictionary<byte, IOperationHandler> _handlers = [];
    readonly Action<string>? _log;
    readonly Session _session;

    public OperationRouter(PhotonServer server, Session session, Action<string>? log = null)
    {
        _log = log;
        _session = session;
        Register(new Join(session));
        Register(new Leave());
        Register(new GetCreditStatus(session));
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
        Register(new RequestStreamingAssetList(session));
        Register(new RequestStreamingAssetInventory(session));
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
        Register(new SyncronizePing());
        Register(new Notification(session));
        Register(new UploadBytes(session));
        Register(new LogicActivateRequest(session));
        Register(new GetResetAvatar(session));
        Register(new PostChatMsg(session));
        Register(new JoinNotification());
        Register(new GetPlanetOwnerships(session));
        Register(new GetAllPlanetPermissions(session));
        Register(new SetProfileSettings(session));
        Register(new GetRewardList());
        Register(new GetRewardIndex(session));
        Register(new ClaimReward(session));
        Register(new GetActorOffer());
        Register(new ClaimActorOffer());
        Register(new CloneTempWorldObjectWithOriginalReference(session));
        Register(new SetFirstTimeEvent(session));
        Register(new OverrideFirstTimeEvent(session));
        Register(new ResetFirstTimeEvents(session));
        Register(new AvatarGesture(session, Kogama.OperationCode.UpdateHeadRotation, Kogama.EventCode.UpdateHeadRotation, reliable: false));
        Register(new AvatarGesture(session, Kogama.OperationCode.UpdatePointingAndHeadRotation, Kogama.EventCode.UpdatePointingAndHeadRotation, reliable: false));
        Register(new AvatarGesture(session, Kogama.OperationCode.StartHeadShake, Kogama.EventCode.StartHeadShake, reliable: false, Api.Gesture.HeadShake));
        Register(new AvatarGesture(session, Kogama.OperationCode.StartHeadNod, Kogama.EventCode.StartHeadNod, reliable: false, Api.Gesture.Nod));
        Register(new AvatarGesture(session, Kogama.OperationCode.StartWave, Kogama.EventCode.StartWave, reliable: false, Api.Gesture.Wave));
        Register(new AvatarGesture(session, Kogama.OperationCode.SetSayChatBubbleVisible, Kogama.EventCode.SetSayChatBubbleVisible, reliable: true));
        Register(new ReportReachedFinishLine(session));
        Register(new GetThemesData(session));
        Register(new AdminOperation(session));
        Register(new OwnerOperation(session));
        Register(new ReportReachedFinishLine(session, Kogama.OperationCode.ReportReachedTimeAttackFlag));
        Register(new RequestAccessoryData(session));
        Register(new SetActiveSpawnRole(session));
        Register(new CreateSpawnRole(session));
        Register(new GetAvatarBodies(session));
        Register(new SetSpawnRoleBody(session));
        Register(new Ignore(Kogama.OperationCode.CustomDevCommands));
        Register(new Ignore(Kogama.OperationCode.ClaimRewardedAdXP));
        Register(new Ignore(Kogama.OperationCode.ClaimPlayingNewGameRewardedGold));
        Register(new Ignore(Kogama.OperationCode.IncrementStatRequest));
        Register(new GameTierOperation(session, Kogama.OperationCode.TogglePreviewTierOperation));
        Register(new UnEquipAccessory(session));
        Register(new UpdateAvatarAccessoryScale(session));
        Register(new Acknowledge(Kogama.OperationCode.ResetHighlights));
        Register(new Acknowledge(Kogama.OperationCode.SetHighlightToSeen));
        Register(new Acknowledge(Kogama.OperationCode.SetMouseSensitivity));
        Register(new Ignore(Kogama.OperationCode.StartSessionTime));
        Register(new GameTierOperation(session, Kogama.OperationCode.ResetPlayerPlanetData));
        Register(new GameTierOperation(session, Kogama.OperationCode.SetGamePassTierOperation));
        Register(new GameTierOperation(session, Kogama.OperationCode.SetGamePassTierToSeenOperation));
        Register(new ClaimWelcomeReward(session));
        Register(new HighScoreList(session, Kogama.OperationCode.GetHighScoreList, Kogama.EventCode.HighScores, top: false));
        Register(new HighScoreList(session, Kogama.OperationCode.GetTopHighScoreList, Kogama.EventCode.TopHighScores, top: true));
        Register(new UpdateGold(session));
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
        Register(new GameCoinBooster(session));
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
        Register(new VehicleEnergyUse(server, session));
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

    internal static string Describe(object? value) => value switch
    {
        null => "null",
        Dictionary<byte, object?> table => "{" + string.Join(", ", table.Select(pair => $"{(Kogama.ParameterKey)pair.Key}={Describe(pair.Value)}")) + "}",
        PhotonDictionary table => "{" + string.Join(", ", table.Entries.Select(pair => $"{pair.Key}={Describe(pair.Value)}")) + "}",
        Dictionary<object, object?> table => "{" + string.Join(", ", table.Select(pair => $"{pair.Key}={Describe(pair.Value)}")) + "}",
        byte[] bytes => $"byte[{bytes.Length}]",
        string text => $"\"{(text.Length > 40 ? text[..40] + "…" : text)}\"",
        Array items => $"{value.GetType().Name}[{items.Length}]",
        _ => $"{value.GetType().Name}:{value}",
    };

    void Refuse(PhotonPeer peer, OperationRequest request)
    {
        var code = (Kogama.OperationCode)request.OperationCode;
        Player? player = _session.For(peer);
        if (AntiCheat.FirstRefusal(player, request.OperationCode))
            _log?.Invoke($"peer {peer.Id}: anti-cheat refused {code} from {player?.Username}");
        if (code is not (Kogama.OperationCode.UpdateWorldObject or Kogama.OperationCode.RuntimeEvent))
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
    }

    internal static readonly bool Trace = Environment.GetEnvironmentVariable("OPENKOGAMA_TRACE") == "1";

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (Trace) _log?.Invoke($"peer {peer.Id}: op {(Kogama.OperationCode)request.OperationCode} {Describe(request.Parameters)}");
        if (!_handlers.TryGetValue(request.OperationCode, out IOperationHandler? handler))
        {
            _log?.Invoke($"peer {peer.Id}: no handler for {request}");
            return;
        }

        try
        {
            if (!AntiCheat.Allows(_session, _session.For(peer), request))
            {
                Refuse(peer, request);
                return;
            }
            handler.Handle(peer, request);
        }
        catch (Exception error)
        {
            _log?.Invoke($"peer {peer.Id}: op {request.OperationCode} failed: {error}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
        }
    }
}
