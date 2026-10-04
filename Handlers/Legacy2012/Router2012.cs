using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Legacy2012;

public sealed class Router2012
{
    const string Version = "2012";
    const int ItemTypeCubeModel = 1;
    const int PlanetImage = 0;
    const int PickupRespawnMs = 3000;

    readonly Session session;
    readonly Dictionary<short, Queue<Dictionary<object, object?>>> _objects = [];
    readonly Dictionary<short, Queue<Dictionary<object, object?>>> _prototypes = [];
    readonly object _sync = new();
    readonly HashSet<int> _takenPickups = [];

    public Router2012(Session session)
    {
        this.session = session;
        session.Logic.DataChanged = SendData;
        session.Round.StateChanged += SendGameState;
    }

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        switch ((Op2012)request.OperationCode)
        {
            case Op2012.Join: Join(peer, request); break;
            case Op2012.Leave: Reply(peer, request); peer.Disconnect(); break;
            case Op2012.DBQuery: DBQuery(peer, request); break;
            case Op2012.LargeDBQuery: Reply(peer, request, new() { [(byte)Key2012.LargeDBQueryID] = 1 }); break;
            case Op2012.GetNextResultSet: GetNextResultSet(peer, request); break;
            case Op2012.RequestPrototypes: RequestPrototypes(peer, request); break;
            case Op2012.GetNextPrototypeBatch: NextBatch(peer, request, _prototypes, Key2012.PrototypeBatchData, Key2012.NumPrototypesToReturnPerBatch); break;
            case Op2012.RequestUserList: RequestUserList(peer, request); break;
            case Op2012.RequestFriends: RequestFriends(peer, request); break;
            case Op2012.RequestFriendshipByName: RequestFriendship(peer, request, byName: true); break;
            case Op2012.RequestFriendshipByProfileID: RequestFriendship(peer, request, byName: false); break;
            case Op2012.RequestAcceptFriendship: AnswerFriendship(peer, request, accept: true); break;
            case Op2012.RequestRejectFriendship: AnswerFriendship(peer, request, accept: false); break;
            case Op2012.PublishPlanet: PublishPlanet(peer, request); break;
            case Op2012.UploadPlanetScreenshot: UploadPlanetScreenshot(peer, request); break;
            case Op2012.RequestWorldObjects: RequestWorldObjects(peer, request); break;
            case Op2012.GetNextWOBatch: NextBatch(peer, request, _objects, Key2012.WOBatchData, Key2012.NumWOToReturnPerBatch); break;
            case Op2012.RequestLinks: RequestLinks(peer, request); break;
            case Op2012.RegisterWorldObject: RegisterWorldObject(peer, request); break;
            case Op2012.UnregisterWorldObject: UnregisterWorldObject(peer, request); break;
            case Op2012.AddLink: AddLink(peer, request); break;
            case Op2012.RemoveLink: RemoveLink(peer, request); break;
            case Op2012.ResetLogicChunk: ResetLogicChunk(request); break;
            case Op2012.UpdateWorldObject: UpdateWorldObject(peer, request); break;
            case Op2012.UpdateWorldObjectData: UpdateWorldObjectData(peer, request); break;
            case Op2012.TransferOwnership: TransferOwnership(peer, request); break;
            case Op2012.UpdatePrototype: UpdatePrototype(peer, request); break;
            case Op2012.RegisterLocalPrototype: RegisterLocalPrototype(peer, request); break;
            case Op2012.RegisterPrototype: RegisterPrototype(peer, request); break;
            case Op2012.AddPrototypeToInventory: AddPrototypeToInventory(peer, request); break;
            case Op2012.RemoveItemFromInventory: RemoveItemFromInventory(peer, request); break;
            case Op2012.UpdateInventorySlots: UpdateInventorySlots(peer, request); break;
            case Op2012.PurchaseItem: Reply(peer, request, returnCode: -4); break;
            case Op2012.AddItemToQuickSlot or Op2012.RemoveItemFromQuickSlot or Op2012.UpdateQuickSlot or Op2012.RequestQuickSlots: Reply(peer, request); break;
            case Op2012.UpdatePrototypeScale: UpdatePrototypeScale(peer, request); break;
            case Op2012.RequestWoUniquePrototype: RequestWoUniquePrototype(peer, request); break;
            case Op2012.TriggerBoxEnter: TriggerBox(peer, request, entering: true); break;
            case Op2012.TriggerBoxExit: TriggerBox(peer, request, entering: false); break;
            case Op2012.UpdateWorldObjectRunTimeData: UpdateWorldObjectRunTimeData(peer, request); break;
            case Op2012.UpdateLineOfFire: Relay(peer, request, Event2012.UpdateLineOfFire, reliable: false); break;
            case Op2012.SetActorReady:
                if (session.For(peer) is Player ready) ready.InWorld = true;
                Reply(peer, request);
                if (session.For(peer) is Player joined) Plugins.PluginHost.Joined(session, joined);
                break;
            case Op2012.RaiseEvent: RaiseEvent(peer, request); break;
            case Op2012.SetProperties: SetProperties(peer, request); break;
            case Op2012.GetProperties: Reply(peer, request); break;
            case Op2012.UpdateNetworkInput: Relay(peer, request, Event2012.UpdateNetworkInput); break;
            case Op2012.UpdateTerrain: Relay(peer, request, Event2012.UpdateTerrain); Reply(peer, request); break;
            case Op2012.Ungroup: Ungroup(peer, request); break;
            case Op2012.LockHierarchy: LockHierarchy(peer, request); break;
            case Op2012.SendChatMsg: SendChatMsg(peer, request); break;
            case Op2012.ReportCaptureFlag: ReportCaptureFlag(peer, request); break;
            case Op2012.RemoveCubesWithinRadius: RemoveCubesWithinRadius(peer, request); break;
            default:
                Console.WriteLine($"peer {peer.Id}: 2012 op {request.OperationCode} is not handled yet");
                Reply(peer, request);
                break;
        }
    }

    static void Reply(PhotonPeer peer, OperationRequest request, Dictionary<byte, object?>? parameters = null, short returnCode = 0) =>
        peer.Send(new OperationResponse(request) { ReturnCode = returnCode, Parameters = parameters ?? [] });

    void Join(PhotonPeer peer, OperationRequest request)
    {
        Player player = session.For(peer) ?? session.Add(peer, buildAvatar: false);
        if (int.TryParse(request[(byte)Key2012.Username] as string, out int profile) && profile > 0)
            player.ProfileId = profile;

        Reply(peer, request, new()
        {
            [(byte)Key2012.ProfileID] = player.ProfileId,
            [(byte)Key2012.ActorNr] = player.Actor,
            [(byte)Key2012.PlanetOwnershipType] = (int)session.OwnershipOf(player),
            [(byte)Key2012.GameStateType] = (int)session.Round.State,
            [(byte)Key2012.GameStateStartTime] = session.Round.StartTime,
            [(byte)Key2012.GameStateDuration] = session.Round.Duration,
            [(byte)Key2012.GameStateReason] = (int)session.Round.Reason,
        });
        Broadcast(peer, new EventData((byte)Event2012.Join)
        {
            Parameters =
            {
                [(byte)Key2012.Username] = player.Username,
                [(byte)Key2012.ProfileID] = player.ProfileId,
                [(byte)Key2012.ActorNr] = player.Actor,
            },
        });
        Console.WriteLine($"peer {peer.Id}: 2012 client joined as actor {player.Actor}");
    }

    public void Left(Player gone)
    {
        lock (_sync)
        {
            _objects.Remove(gone.Peer.Id);
            _prototypes.Remove(gone.Peer.Id);
        }
        foreach (int trigger in session.Triggers.ExitAll(gone.Actor))
            Broadcast(gone.Peer, new EventData((byte)Event2012.TriggerBoxStayEnd) { Parameters = { [(byte)Key2012.WorldObjectID] = trigger } });
        if (gone.AvatarId >= 0)
            Broadcast(gone.Peer, new EventData((byte)Event2012.UnregisterWorldObject) { Parameters = { [(byte)Key2012.WorldObjectID] = gone.AvatarId } });
        Broadcast(gone.Peer, new EventData((byte)Event2012.Leave) { Parameters = { [(byte)Key2012.ActorNr] = gone.Actor } });
    }

    void Broadcast(PhotonPeer? from, EventData evt, bool reliable = true)
    {
        foreach (Player player in session.Players)
            if (player.Peer != from)
                player.Peer.Send(evt, reliable: reliable);
    }

    void DBQuery(PhotonPeer peer, OperationRequest request)
    {
        var outData = new Dictionary<object, object?>();
        switch ((DBQuery2012)Convert.ToByte(request[(byte)Key2012.DBQuery]))
        {
            case DBQuery2012.RequestItemTypes:
                foreach ((int id, string name) in Items.Catalog.Categories) outData[id] = name;
                break;
            case DBQuery2012.RequestPlanetOwnershipTypes:
                foreach (PlanetOwnership ownership in Enum.GetValues<PlanetOwnership>()) outData[(int)ownership] = ownership.ToString();
                break;
        }

        Reply(peer, request, new()
        {
            [(byte)Key2012.DBQueryReturnCode] = (byte)0,
            [(byte)Key2012.DBQueryOutData] = outData,
        });
    }

    void GetNextResultSet(PhotonPeer peer, OperationRequest request) =>
        Reply(peer, request, new()
        {
            [(byte)Key2012.DBQueryOutData] = Inventory(session.For(peer)),
            [(byte)Key2012.LargeDBQueryID] = Convert.ToInt32(request[(byte)Key2012.LargeDBQueryID]),
            [(byte)Key2012.HasMoreResultSets] = false,
        });

    void RequestPrototypes(PhotonPeer peer, OperationRequest request)
    {
        var batch = new Queue<Dictionary<object, object?>>(session.World.ToSnapshot().Prototypes.Select(prototype => new Dictionary<object, object?>
        {
            [(byte)Key2012.WorldInventoryID] = prototype.Id,
            [(byte)Key2012.ItemID] = -1,
            [(byte)Key2012.ItemTypeID] = ItemTypeCubeModel,
            [(byte)Key2012.WorldInventoryName] = "",
            [(byte)Key2012.WorldInventoryData] = new Dictionary<object, object?> { [(byte)Key2012.PrototypeData] = prototype.Cubes.ToBytes() },
            [(byte)Key2012.Scale] = prototype.Scale,
        }));
        int count = batch.Count;
        lock (_sync) _prototypes[peer.Id] = batch;

        Reply(peer, request, new()
        {
            [(byte)Key2012.NumPrototypes] = count,
            [(byte)Key2012.PrototypeBatchQueryID] = 1,
        });
        Console.WriteLine($"peer {peer.Id}: 2012 client gets {count} prototypes");
    }

    void RequestWorldObjects(PhotonPeer peer, OperationRequest request)
    {
        IReadOnlyList<WorldObject> objects = session.WithoutNpcs(session.World.ToSnapshot()).Objects;
        var batch = new Queue<Dictionary<object, object?>>(WorldObjects2012.Describe(objects, ProtocolTable.For(Version)));
        int count = batch.Count;
        var kept = batch.Select(obj => (int)obj[(byte)Key2012.WorldObjectID]!).ToHashSet();
        string dropped = string.Join(", ", objects.Where(obj => !kept.Contains(obj.Id)).GroupBy(obj => obj.Type).Select(group => $"{group.Key} x{group.Count()}"));
        if (dropped.Length > 0) Console.WriteLine($"peer {peer.Id}: 2012 client cannot show {dropped}");
        lock (_sync) _objects[peer.Id] = batch;

        Reply(peer, request, new()
        {
            [(byte)Key2012.NumWorldObjects] = count,
            [(byte)Key2012.WOBatchQueryID] = 1,
        });
        Console.WriteLine($"peer {peer.Id}: 2012 client gets {count} world objects");
    }

    void NextBatch(PhotonPeer peer, OperationRequest request, Dictionary<short, Queue<Dictionary<object, object?>>> batches, Key2012 dataKey, Key2012 sizeKey)
    {
        int size = request[(byte)sizeKey] is { } requested ? Math.Max(1, Convert.ToInt32(requested)) : 50;
        var page = new Dictionary<object, object?>();
        lock (_sync)
        {
            if (batches.TryGetValue(peer.Id, out var queue))
                for (int index = 0; index < size && queue.Count > 0; index++)
                    page[index] = queue.Dequeue();
        }
        Reply(peer, request, new() { [(byte)dataKey] = page });
    }

    void RequestUserList(PhotonPeer peer, OperationRequest request)
    {
        var users = new Dictionary<object, object?>();
        foreach (Player player in session.Players)
        {
            users[player.Actor] = new Dictionary<object, object?>
            {
                [(byte)Key2012.Username] = player.Username,
                [(byte)Key2012.ProfileID] = player.ProfileId,
                [(byte)Key2012.AvatarStatus] = new Dictionary<object, object?> { ["health"] = 100f },
            };
        }
        Reply(peer, request, new() { [(byte)Key2012.UserList] = users });
    }

    void RequestLinks(PhotonPeer peer, OperationRequest request)
    {
        Snapshot snapshot = session.World.ToSnapshot();
        var sent = snapshot.Objects.Select(obj => obj.Id).ToHashSet();
        int[] table = [.. snapshot.Links
            .Where(link => sent.Contains(link.From) && sent.Contains(link.To))
            .SelectMany(link => new[] { link.Id, link.From, link.To, 1 })];
        Reply(peer, request, new() { [(byte)Key2012.LinkTable] = table });
    }

    void RequestFriends(PhotonPeer peer, OperationRequest request)
    {
        int profile = session.For(peer)?.ProfileId ?? 0;
        var friends = new Dictionary<object, object?>();
        foreach (Friendship friendship in Stores.Friends.Friends(profile))
        {
            bool mine = friendship.Profile == profile || friendship.Status == FriendStatus.Accepted;
            int other = friendship.Profile == profile ? friendship.Friend : friendship.Profile;
            friends[friendship.Id] = new Dictionary<object, object?>
            {
                [(byte)DBQueryKey2012.ProfileID] = mine ? profile : friendship.Profile,
                [(byte)DBQueryKey2012.FriendProfileID] = mine ? other : profile,
                [(byte)DBQueryKey2012.FriendStatus] = (int)friendship.Status,
            };
        }
        Reply(peer, request, new() { [(byte)Key2012.Friends] = friends });
    }

    void RequestFriendship(PhotonPeer peer, OperationRequest request, bool byName)
    {
        if (session.For(peer) is not Player player)
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        int? target = byName
            ? session.Players.FirstOrDefault(other => other.Username == request[(byte)Key2012.Username] as string)?.ProfileId
            : Convert.ToInt32(request[(byte)Key2012.FriendProfileID]);

        short code = target switch
        {
            null => -2,
            int other when other == player.ProfileId => -1,
            int other => Stores.Friends.Between(player.ProfileId, other) switch
            {
                null => 0,
                { Status: FriendStatus.Accepted } => -4,
                { Profile: var from } when from == player.ProfileId => -3,
                _ => -6,
            },
        };
        if (code != 0)
        {
            Reply(peer, request, returnCode: code);
            return;
        }

        Friendship friendship = Stores.Friends.Request(player.ProfileId, target!.Value);
        Reply(peer, request);
        SendToFriends(friendship, new EventData((byte)Event2012.FriendRequest)
        {
            Parameters =
            {
                [(byte)Key2012.FriendID] = friendship.Id,
                [(byte)Key2012.ProfileID] = friendship.Profile,
                [(byte)Key2012.FriendProfileID] = friendship.Friend,
            },
        });
    }

    void AnswerFriendship(PhotonPeer peer, OperationRequest request, bool accept)
    {
        int id = Convert.ToInt32(request[(byte)Key2012.FriendID]);
        if (session.For(peer) is not Player player || Stores.Friends.Find(id) is not Friendship friendship
            || friendship.Friend != player.ProfileId || friendship.Status != FriendStatus.Pending)
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        if (accept) Stores.Friends.SetStatus(id, FriendStatus.Accepted);
        else Stores.Friends.Remove(id);

        Reply(peer, request);
        SendToFriends(friendship, new EventData((byte)Event2012.FriendUpdate)
        {
            Parameters =
            {
                [(byte)Key2012.FriendID] = id,
                [(byte)Key2012.ProfileID] = friendship.Profile,
                [(byte)Key2012.FriendStatusID] = (int)(accept ? FriendStatus.Accepted : FriendStatus.Rejected),
            },
        });
    }

    void SendToFriends(Friendship friendship, EventData evt)
    {
        foreach (Player other in session.Players)
            if (other.ProfileId == friendship.Profile || other.ProfileId == friendship.Friend)
                other.Peer.Send(evt);
    }

    void PublishPlanet(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || session.WorldId is not int world
            || session.Play || session.OwnershipOf(player) != PlanetOwnership.Owner)
        {
            Reply(peer, request, returnCode: -2);
            return;
        }

        if (request[(byte)Key2012.PlanetTextureData] is byte[] { Length: > 0 } png && Stores.Images.Image(PlanetImage, world) is null)
            Stores.Images.SaveImage(PlanetImage, world, png);

        session.Publish();
        Reply(peer, request);
    }

    void UploadPlanetScreenshot(PhotonPeer peer, OperationRequest request)
    {
        if (session.WorldId is int world && request[(byte)Key2012.PlanetTextureData] is byte[] { Length: > 0 } png)
        {
            Stores.Images.SaveImage(PlanetImage, world, png);
            Console.WriteLine($"peer {peer.Id}: planet screenshot for world {world} ({png.Length} bytes)");
        }
        Reply(peer, request);
    }

    void UpdateWorldObject(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (session.World.Find(objectId) is { Type: not WorldObjectType.Avatar })
        {
            session.World.Modify(objectId, obj =>
            {
                obj.Position = [Convert.ToSingle(request[(byte)Key2012.PosX]), Convert.ToSingle(request[(byte)Key2012.PosY]), Convert.ToSingle(request[(byte)Key2012.PosZ])];
                obj.Rotation = [Convert.ToSingle(request[(byte)Key2012.RotX]), Convert.ToSingle(request[(byte)Key2012.RotY]), Convert.ToSingle(request[(byte)Key2012.RotZ]), Convert.ToSingle(request[(byte)Key2012.RotW])];
            });
            session.World.MarkChanged();
        }

        Broadcast(peer, new EventData((byte)Event2012.UpdateWorldObject) { Parameters = new Dictionary<byte, object?>(request.Parameters) }, reliable: false);
    }

    void TriggerBox(PhotonPeer peer, OperationRequest request, bool entering)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (session.For(peer) is not Player player || session.World.Find(objectId) is not WorldObject obj) return;

        if (obj.Type is WorldObjectType.PickupItemHealthPack or WorldObjectType.PickupItemCenterGun)
        {
            if (entering) TakePickup(objectId, player.Actor);
            return;
        }

        bool changed = entering ? session.Triggers.Enter(objectId, player.Actor) : session.Triggers.Exit(objectId, player.Actor);
        var parameters = new Dictionary<byte, object?>
        {
            [(byte)Key2012.WorldObjectID] = objectId,
            [(byte)Key2012.ActorNr] = player.Actor,
        };

        Broadcast(null, new EventData((byte)(entering ? Event2012.TriggerBoxEnter : Event2012.TriggerBoxExit)) { Parameters = new(parameters) });
        if (changed)
        {
            Broadcast(null, new EventData((byte)(entering ? Event2012.TriggerBoxStayBegin : Event2012.TriggerBoxStayEnd)) { Parameters = new(parameters) });
            session.Logic.Evaluate();
        }
    }

    void TakePickup(int objectId, int actor)
    {
        lock (_sync)
            if (!_takenPickups.Add(objectId)) return;

        SendPickupState(objectId, actor, PickupItemState.Pickup);
        _ = Task.Delay(PickupRespawnMs).ContinueWith(_ =>
        {
            lock (_sync) _takenPickups.Remove(objectId);
            SendPickupState(objectId, actor, PickupItemState.Listening);
        });
    }

    void SendPickupState(int objectId, int actor, PickupItemState state) =>
        Broadcast(null, new EventData((byte)Event2012.PickupItemStateChange)
        {
            Parameters =
            {
                [(byte)Key2012.PickupItemState] = (int)state,
                [(byte)Key2012.WorldObjectID] = objectId,
                [(byte)Key2012.ActorNr] = actor,
            },
        });

    void RaiseEvent(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData(Convert.ToByte(request[(byte)LiteKey2012.Code]))
        {
            Parameters =
            {
                [(byte)LiteKey2012.Data] = request[(byte)LiteKey2012.Data],
                [(byte)LiteKey2012.ActorNr] = session.For(peer)?.Actor ?? 0,
            },
        };
        Broadcast(peer, evt);
    }

    void SetProperties(PhotonPeer peer, OperationRequest request)
    {
        Reply(peer, request);
        if (request[(byte)LiteKey2012.Broadcast] is not true) return;

        Broadcast(null, new EventData((byte)Event2012.PropertiesChanged)
        {
            Parameters =
            {
                [(byte)Key2012.TargetActorNr] = Convert.ToInt32(request[(byte)LiteKey2012.ActorNr] ?? 0),
                [(byte)Key2012.Properties] = request[(byte)LiteKey2012.Properties],
            },
        });
    }

    void Ungroup(PhotonPeer peer, OperationRequest request)
    {
        int groupId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (session.World.Find(groupId) is not { ParentId: not -1 } group)
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        foreach (WorldObject child in session.World.Subtree(groupId).Where(obj => obj.ParentId == groupId).ToList())
            session.World.Reparent(child.Id, group.ParentId);
        session.World.Remove(groupId);
        session.World.MarkChanged();

        Reply(peer, request);
        Broadcast(peer, new EventData((byte)Event2012.Ungroup) { Parameters = { [(byte)Key2012.WorldObjectID] = groupId } });
    }

    void LockHierarchy(PhotonPeer peer, OperationRequest request)
    {
        int id = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        bool locked = request[(byte)Key2012.Lock] is true;
        int? owner = locked ? session.For(peer)?.Actor ?? 0 : null;

        List<WorldObject> targets = [.. session.World.Subtree(id).Where(obj => obj.Id == id || obj.ParentId == id)];
        foreach (WorldObject target in targets)
            session.World.Modify(target.Id, obj => obj.Owner = owner);

        Reply(peer, request, new()
        {
            [(byte)Key2012.WorldObjectID] = id,
            [(byte)Key2012.Lock] = locked,
        }, targets.Count > 0 ? (short)0 : (short)-1);
        Broadcast(peer, new EventData((byte)Event2012.LockHierarchy)
        {
            Parameters =
            {
                [(byte)Key2012.WorldObjectID] = id,
                [(byte)Key2012.OwnerActorNr] = owner ?? 0,
            },
        });
    }

    void SendChatMsg(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || request[(byte)Key2012.ChatMsg] is not string message) return;

        if (Plugins.PluginHost.Chat(session, player, message) is not { } allowed) return;
        message = allowed;
        if (message.Length > 256) message = message[..256];
        Broadcast(null, new EventData((byte)Event2012.ChatMsg)
        {
            Parameters =
            {
                [(byte)Key2012.ActorNr] = player.Actor,
                [(byte)Key2012.ChatMsg] = message,
            },
        });
        Console.WriteLine($"peer {peer.Id}: chat {player.Username}: {message}");
    }

    void ReportCaptureFlag(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player)
            session.Round.CaptureFlag(player);
        Reply(peer, request);
    }

    void SendGameState(int actor) =>
        Broadcast(null, new EventData((byte)Event2012.GameStateChange)
        {
            Parameters =
            {
                [(byte)Key2012.GameStateType] = (int)session.Round.State,
                [(byte)Key2012.GameStateStartTime] = session.Round.StartTime,
                [(byte)Key2012.GameStateDuration] = session.Round.Duration,
                [(byte)Key2012.GameStateReason] = (int)session.Round.Reason,
                [(byte)Key2012.ActorNr] = actor,
            },
        });

    void RemoveCubesWithinRadius(PhotonPeer peer, OperationRequest request)
    {
        Broadcast(null, new EventData((byte)Event2012.RemoveCubesWithinRadius)
        {
            Parameters =
            {
                [(byte)Key2012.Radius] = request[(byte)Key2012.Radius],
                [(byte)Key2012.PosX] = request[(byte)Key2012.PosX],
                [(byte)Key2012.PosY] = request[(byte)Key2012.PosY],
                [(byte)Key2012.PosZ] = request[(byte)Key2012.PosZ],
                [(byte)Key2012.Damage] = request[(byte)Key2012.Damage],
                [(byte)Key2012.FallOffType] = request[(byte)Key2012.FallOffType],
            },
        });
        Reply(peer, request);
    }

    void UpdateWorldObjectData(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        var data = WorldObjects2012.Packed(request[(byte)Key2012.WorldObjectData]);
        if (!session.World.Modify(objectId, obj => obj.Data = data))
        {
            Console.WriteLine($"peer {peer.Id}: data for unknown object {objectId}");
            return;
        }

        session.World.MarkChanged();
        SendData(objectId);
        session.Logic.Evaluate(react: false);
    }

    void SendData(int objectId)
    {
        if (session.World.Find(objectId) is not WorldObject obj || WorldObjects2012.Data(obj) is not { } data) return;
        var evt = new EventData((byte)Event2012.UpdateWorldObjectData)
        {
            Parameters =
            {
                [(byte)Key2012.WorldObjectID] = objectId,
                [(byte)Key2012.WorldObjectData] = WorldObjects2012.Table(data),
            },
        };
        foreach (Player player in session.Players)
            if (player.InWorld)
                player.Peer.Send(evt);
    }

    void UpdatePrototype(PhotonPeer peer, OperationRequest request)
    {
        int prototypeId = Convert.ToInt32(request[(byte)Key2012.WorldInventoryID]);
        if (session.World.FindPrototype(prototypeId) is not Prototype prototype || request[(byte)Key2012.WorldInventoryData] is not byte[] changes)
        {
            Console.WriteLine($"peer {peer.Id}: unknown prototype {prototypeId}");
            return;
        }

        prototype.Cubes.Apply(changes);
        session.World.MarkChanged();
        Broadcast(peer, new EventData((byte)Event2012.UpdatePrototype) { Parameters = new Dictionary<byte, object?>(request.Parameters) });
    }

    static Dictionary<object, object?> Inventory(Player? player)
    {
        var items = new Dictionary<object, object?>();
        if (player is null) return items;

        foreach ((Item item, int slot, _) in Inventories.WithSlots(player.ProfileId))
        {
            if (ModelInventory.SinglePrototype(item) is not Prototype prototype) continue;
            items[item.Id] = new Dictionary<object, object?>
            {
                [(byte)DBQueryKey2012.ItemTypeID] = ItemTypeCubeModel,
                [(byte)DBQueryKey2012.ItemName] = item.Name,
                [(byte)DBQueryKey2012.ItemData] = prototype.Cubes.ToBytes(),
                [(byte)DBQueryKey2012.SlotIndex] = slot,
            };
        }
        return items;
    }

    void RegisterPrototype(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)Key2012.ItemID]);
        if (session.For(peer) is not Player player || Inventories.Find(player.ProfileId, itemId) is not Item item
            || ModelInventory.SinglePrototype(item) is not Prototype stored)
        {
            Console.WriteLine($"peer {peer.Id}: no model item {itemId} to place");
            Reply(peer, request, returnCode: -1);
            return;
        }

        var prototype = new Prototype(session.World.NewPrototypeId(), stored.Scale, item.Author, stored.Cubes.Clone());
        session.World.Add(prototype);
        session.World.MarkChanged();

        Reply(peer, request, new() { [(byte)Key2012.WorldInventoryID] = prototype.Id });
        Broadcast(null, RegisteredPrototype(prototype, itemId, item.Name, player));
    }

    void AddPrototypeToInventory(PhotonPeer peer, OperationRequest request)
    {
        int prototypeId = Convert.ToInt32(request[(byte)Key2012.WorldInventoryID]);
        short code = 0;
        int itemId = -1;
        if (session.For(peer) is not Player player) code = -1;
        else if (session.World.FindPrototype(prototypeId) is not Prototype prototype) code = -2;
        else if (prototype.AuthorId != player.ProfileId) code = -3;
        else
        {
            Item item = Inventories.Add(player.ProfileId, ModelInventory.ModelName, ModelInventory.ModelCategory, ModelInventory.Pack(prototype, [1f, 1f, 1f]), prototype.AuthorId);
            itemId = item.Id;
            int slot = Inventories.WithSlots(player.ProfileId).First(entry => entry.Item.Id == item.Id).Slot;
            Broadcast(null, new EventData((byte)Event2012.AddItemToInventory)
            {
                Parameters =
                {
                    [(byte)Key2012.ActorNr] = player.Actor,
                    [(byte)Key2012.ItemID] = item.Id,
                    [(byte)Key2012.ItemTypeID] = ItemTypeCubeModel,
                    [(byte)Key2012.ItemName] = item.Name,
                    [(byte)Key2012.ItemData] = prototype.Cubes.ToBytes(),
                    [(byte)Key2012.SlotIndex] = slot,
                    [(byte)Key2012.WorldInventoryID] = prototypeId,
                },
            });
            Console.WriteLine($"peer {peer.Id}: added prototype {prototypeId} to inventory of profile {player.ProfileId} as item {item.Id}");
        }

        Reply(peer, request, new()
        {
            [(byte)Key2012.ItemPrice] = 0,
            [(byte)Key2012.ItemID] = itemId,
            [(byte)Key2012.WorldInventoryID] = prototypeId,
        }, code);
    }

    void RemoveItemFromInventory(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)Key2012.ItemID]);
        if (session.For(peer) is not Player player || !Inventories.Remove(player.ProfileId, itemId))
        {
            Console.WriteLine($"peer {peer.Id}: item {itemId} is not removable");
            Reply(peer, request, returnCode: -1);
            return;
        }

        peer.Send(new EventData((byte)Event2012.RemoveItemFromInventory) { Parameters = { [(byte)Key2012.ItemID] = itemId } });
        Reply(peer, request);
    }

    void UpdateInventorySlots(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player && PhotonValues.Normalize(request[(byte)Key2012.ItemIDToSlotIndexTable]) is Dictionary<object, object?> table)
        {
            Dictionary<int, int> slots = Stores.Profiles.Slots(player.ProfileId);
            foreach ((object item, object? slot) in table)
                slots[Convert.ToInt32(item)] = Convert.ToInt32(slot);
            Inventories.SetSlots(player.ProfileId, slots);
        }
        Reply(peer, request);
    }

    static EventData RegisteredPrototype(Prototype prototype, int itemId, string name, Player player) =>
        new((byte)Event2012.RegisterPrototype)
        {
            Parameters =
            {
                [(byte)Key2012.WorldInventoryID] = prototype.Id,
                [(byte)Key2012.ItemID] = itemId,
                [(byte)Key2012.ItemTypeID] = ItemTypeCubeModel,
                [(byte)Key2012.WorldInventoryName] = name,
                [(byte)Key2012.WorldInventoryData] = new Dictionary<object, object?> { [(byte)Key2012.PrototypeData] = prototype.Cubes.ToBytes() },
                [(byte)Key2012.Scale] = prototype.Scale,
                [(byte)Key2012.ActorNr] = player.Actor,
            },
        };

    void RegisterLocalPrototype(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || request[(byte)Key2012.WorldInventoryData] is not byte[] cubes)
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        float scale = request[(byte)Key2012.Scale] is { } value ? Convert.ToSingle(value) : 1f;
        var prototype = new Prototype(session.World.NewPrototypeId(), scale, player.ProfileId, CubeModel.FromBytes(cubes));
        session.World.Add(prototype);
        session.World.MarkChanged();

        Reply(peer, request, new() { [(byte)Key2012.WorldInventoryID] = prototype.Id });
        Broadcast(null, RegisteredPrototype(prototype, -1, request[(byte)Key2012.WorldInventoryName] as string ?? "", player));
    }

    void UpdatePrototypeScale(PhotonPeer peer, OperationRequest request)
    {
        int prototypeId = Convert.ToInt32(request[(byte)Key2012.WorldInventoryID]);
        float scale = Convert.ToSingle(request[(byte)Key2012.Scale]);
        if (!float.IsFinite(scale) || scale <= 0f || !session.World.SetPrototypeScale(prototypeId, scale))
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        session.World.MarkChanged();
        Reply(peer, request);
        Broadcast(peer, new EventData((byte)Event2012.UpdatePrototypeScale)
        {
            Parameters =
            {
                [(byte)Key2012.WorldInventoryID] = prototypeId,
                [(byte)Key2012.Scale] = scale,
            },
        });
    }

    void RequestWoUniquePrototype(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (session.World.MakeUniquePrototype(objectId) is not Prototype unique)
        {
            Console.WriteLine($"peer {peer.Id}: no prototype to make unique for {objectId}");
            Reply(peer, request, new() { [(byte)Key2012.WorldObjectID] = objectId }, -1);
            return;
        }

        session.World.MarkChanged();
        Broadcast(null, new EventData((byte)Event2012.WoUniquePrototype)
        {
            Parameters =
            {
                [(byte)Key2012.WorldObjectID] = objectId,
                [(byte)Key2012.WorldInventoryID] = unique.Id,
            },
        });
        Reply(peer, request);
    }

    void TransferOwnership(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        int owner = Convert.ToInt32(request[(byte)Key2012.OwnerActorNr]);
        bool found = session.World.Modify(objectId, obj => obj.Owner = owner == 0 ? null : owner);
        if (found)
            Broadcast(peer, new EventData((byte)Event2012.TransferOwnership) { Parameters = new Dictionary<byte, object?>(request.Parameters) });
        else
            Console.WriteLine($"peer {peer.Id}: transfer ownership of unknown object {objectId}");

        Reply(peer, request, new()
        {
            [(byte)Key2012.WorldObjectID] = objectId,
            [(byte)Key2012.OwnerActorNr] = owner,
        }, found ? (short)0 : (short)-1);
    }

    void UpdateWorldObjectRunTimeData(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (PhotonValues.Normalize(request[(byte)Key2012.WorldObjectRunTimeData]) is Dictionary<object, object?> changes)
        {
            try
            {
                session.World.Modify(objectId, obj => PackedData.Merge(obj.Runtime, changes));
            }
            catch (NotSupportedException error)
            {
                Console.WriteLine($"peer {peer.Id}: runtime of {objectId} not stored, {error.Message}");
            }
        }

        Relay(peer, request, Event2012.UpdateWorldObjectRunTimeData);
    }

    void Relay(PhotonPeer peer, OperationRequest request, Event2012 code, bool reliable = true)
    {
        var evt = new EventData((byte)code) { Parameters = new Dictionary<byte, object?>(request.Parameters) };
        evt.Parameters[(byte)Key2012.ActorNr] = session.For(peer)?.Actor ?? peer.Id;
        Broadcast(peer, evt, reliable);
    }

    void RegisterWorldObject(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player)
        {
            Reply(peer, request, returnCode: -1);
            return;
        }

        WorldObject obj = WorldObjects2012.Read(request, session.World.NewObjectId(), ProtocolTable.For(Version));
        obj.Owner = player.Actor;
        session.World.Add(obj);
        if (obj.Type == WorldObjectType.Avatar) player.AvatarId = obj.Id;
        else session.World.MarkChanged();

        Reply(peer, request, new() { [(byte)Key2012.WorldObjectID] = obj.Id });

        var registered = new EventData((byte)Event2012.RegisterWorldObject);
        foreach ((object key, object? value) in WorldObjects2012.Describe([obj], ProtocolTable.For(Version)).Single())
            registered.Parameters[(byte)key] = value;
        Broadcast(peer, registered);
        Console.WriteLine($"peer {peer.Id}: 2012 client registered {obj.Type} as {obj.Id}");
    }

    void UnregisterWorldObject(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        if (session.World.Find(objectId) is not WorldObject obj || obj.Type == WorldObjectType.Avatar)
        {
            Console.WriteLine($"peer {peer.Id}: cannot delete object {objectId}");
            Reply(peer, request, returnCode: -1);
            return;
        }

        List<int> removedPrototypes = session.World.RemoveTree(objectId);
        session.World.MarkChanged();
        session.Logic.Evaluate();

        Reply(peer, request, new() { [(byte)Key2012.WorldObjectID] = objectId });
        Broadcast(peer, new EventData((byte)Event2012.UnregisterWorldObject) { Parameters = { [(byte)Key2012.WorldObjectID] = objectId } });
        foreach (int prototypeId in removedPrototypes)
            Broadcast(null, new EventData((byte)Event2012.UnregisterPrototype) { Parameters = { [(byte)Key2012.WorldInventoryID] = prototypeId } });
    }

    void AddLink(PhotonPeer peer, OperationRequest request)
    {
        int from = Convert.ToInt32(request[(byte)Key2012.LinkFromID]);
        int to = Convert.ToInt32(request[(byte)Key2012.LinkToID]);
        if (session.World.Find(from) is null || session.World.Find(to) is null)
        {
            Reply(peer, request, new() { [(byte)Key2012.LinkID] = -1 }, -1);
            return;
        }

        Link link = session.World.AddLink(from, to, false);
        session.World.MarkChanged();
        session.Logic.Evaluate();

        Reply(peer, request, new() { [(byte)Key2012.LinkID] = link.Id });
        Broadcast(peer, new EventData((byte)Event2012.AddLink)
        {
            Parameters =
            {
                [(byte)Key2012.LinkID] = link.Id,
                [(byte)Key2012.LinkFromID] = link.From,
                [(byte)Key2012.LinkToID] = link.To,
            },
        });
    }

    void RemoveLink(PhotonPeer peer, OperationRequest request)
    {
        int id = Convert.ToInt32(request[(byte)Key2012.LinkID]);
        if (!session.World.RemoveLink(id, false))
        {
            Reply(peer, request);
            return;
        }

        session.World.MarkChanged();
        session.Logic.Evaluate();
        Reply(peer, request);
        Broadcast(peer, new EventData((byte)Event2012.RemoveLink) { Parameters = { [(byte)Key2012.LinkID] = id } });
    }

    void ResetLogicChunk(OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)Key2012.WorldObjectID]);
        Broadcast(null, new EventData((byte)Event2012.ResetLogicChunk) { Parameters = { [(byte)Key2012.WorldObjectID] = objectId } });
        session.Logic.Reset(session.World.LogicChunk(objectId));
    }
}
