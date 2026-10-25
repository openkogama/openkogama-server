using System.Text.Json.Nodes;
using OpenKogama.Game;
using OpenKogama.Handlers;
using OpenKogama.Handlers.Legacy;
using OpenKogama.Handlers.Legacy2012;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Hosting;

public sealed class SessionHost(PhotonServer server, bool mixedClients)
{
    public Func<int> EntryWorld { get; init; } = () => 0;
    public Func<string, bool> Allows { get; init; } = _ => true;
    sealed record Entry(Session Session, OperationRouter? Router, bool Editor, string Client = "") { public Router2012? Classic { get; set; } }

    readonly object _sync = new();
    readonly Dictionary<(int Id, bool Play, string Client), Entry> _worlds = [];
    readonly Dictionary<PhotonPeer, Entry> _peers = [];

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (peer.Protocol == PhotonProtocol.Protocol15)
        {
            Handle2012(peer, request);
            return;
        }

        if (peer.Translator is null && request.OperationCode == ClientProtocols.Handshake)
        {
            peer.Send(new EventData(ClientProtocols.HandshakeEvent) { [ClientProtocols.HandshakeData] = Guid.NewGuid().ToString("N") });
            return;
        }

        if (peer.Translator is null && request.OperationCode == (byte)OperationCode.Join && ClientProtocols.Detect(request) is { } detected)
        {
            peer.Translator = detected;
            Console.WriteLine($"peer {peer.Id}: client {detected.Version}");
        }

        if (peer.Translator is null && request.OperationCode == (byte)OperationCode.Join && ClientProtocols.Remap(request) is { } remap)
        {
            remap.Peer = peer;
            peer.Translator = remap;
            Console.WriteLine($"peer {peer.Id}: client {remap.Version} with renumbered operations");
        }

        if (peer.Translator is OperationRemap remapped)
        {
            if (remapped.Incoming(request) is not { } mapped)
            {
                Console.WriteLine($"peer {peer.Id}: client op {remapped.ClientOperation(request.OperationCode)} has no server operation");
                return;
            }
            request = mapped;
        }

        if (peer.Translator is LegacyTranslator legacy)
        {
            if (legacy.Incoming(request) is not { } translated)
            {
                Entry? owner;
                lock (_sync) _peers.TryGetValue(peer, out owner);
                if (owner is not null) LegacyOperations.Handle(peer, request, legacy, owner.Session);
                return;
            }
            request = translated;
        }

        Entry? entry;
        lock (_sync)
        {
            if (!_peers.TryGetValue(peer, out entry) && request.OperationCode == (byte)OperationCode.Join)
            {
                entry = Assign(peer, request);
                if (entry is not null) _peers[peer] = entry;
            }
        }

        if (entry?.Router is null)
        {
            Console.WriteLine($"peer {peer.Id}: op {request.OperationCode} rejected, not in a world");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        entry.Router.Handle(peer, request);
    }

    void Handle2012(PhotonPeer peer, OperationRequest request)
    {
        Entry? entry;
        lock (_sync)
        {
            if (!_peers.TryGetValue(peer, out entry) && request.OperationCode == (byte)Op2012.Join)
            {
                entry = Assign2012(peer, request);
                if (entry is not null) _peers[peer] = entry;
            }
        }

        if (entry?.Classic is null)
        {
            Console.WriteLine($"peer {peer.Id}: 2012 op {request.OperationCode} rejected, not in a world");
            peer.Send(new OperationResponse(request) { ReturnCode = -2 });
            return;
        }

        try
        {
            entry.Classic.Handle(peer, request);
        }
        catch (Exception error)
        {
            Console.WriteLine($"peer {peer.Id}: 2012 op {request.OperationCode} failed: {error}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
        }
    }

    Entry? Assign2012(PhotonPeer peer, OperationRequest join)
    {
        const string Version = "2012";
        if (!Allows(Version))
        {
            Console.WriteLine("2012 client rejected, not in server.json clients");
            return null;
        }
        if (!int.TryParse(join[(byte)Key2012.PlanetName] as string, out int id)) return null;

        bool play = join[(byte)Key2012.JoinEditMode] is not true;
        if (play && id == 0) id = EntryWorld();
        string client = mixedClients ? "" : Version;
        if (mixedClients) peer.Translator ??= new Translator2012 { Peer = peer };
        if (_worlds.TryGetValue((id, play, client), out Entry? open))
        {
            open.Classic ??= new Router2012(open.Session);
            if (peer.Translator is Translator2012 shared) shared.Session = open.Session;
            return open;
        }
        int profile = int.TryParse(join[(byte)Key2012.Username] as string, out int parsed) ? parsed : 0;
        if (!play && EditLocked(id) && !TakeOver(id, peer, profile)) return null;
        if (Session.Open(id, play) is not { } session) return null;
        session.Primary = client == "";

        Console.WriteLine($"world {id} {(play ? "play" : "edit")} opened{(client == "" ? "" : $" for {client}")}");
        Entry entry = mixedClients ? Create(session, editor: false) : new Entry(session, null, false, client);
        if (!mixedClients) session.Begin(() => server.Now);
        entry.Classic = new Router2012(session);
        if (peer.Translator is Translator2012 joined) joined.Session = session;
        return _worlds[(id, play, client)] = entry;
    }

    public void Disconnect(PhotonPeer peer)
    {
        Entry? entry;
        lock (_sync)
            if (!_peers.Remove(peer, out entry)) return;

        Session session = entry.Session;
        Player? gone = session.For(peer);
        if (gone is null) return;
        Plugins.PluginHost.Left(session, gone);
        CoinBoost.Stop(gone);

        if (entry.Editor)
        {
            session.SaveEditedAvatar(gone);
            return;
        }

        if (peer.Protocol == PhotonProtocol.Protocol15)
        {
            entry.Classic?.Left(gone);
            if (!mixedClients)
            {
                session.Remove(gone);
                Close(entry);
                return;
            }
        }

        ReleaseEverything(session, gone);
        Colossus.Release(session, gone.AvatarId);
        foreach (int role in gone.ClassAvatars.Values) Colossus.Release(session, role);
        session.Remove(gone);
        session.Round.Stats.RemoveActor(gone.Actor);

        foreach (int trigger in session.Triggers.ExitAll(gone.Actor))
            TriggerBox.Send(session, trigger, gone.Actor, pressed: false);
        session.Logic.Evaluate();

        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.AvatarId },
        };
        var unregisterBuilder = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.BuildAvatarId },
        };
        var leave = new EventData((byte)EventCode.Leave)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = gone.Actor },
        };
        foreach (Player other in session.Players.Where(other => other.Saw(gone.Actor)))
        {
            other.Peer.Send(unregister);
            if (gone.BuildAvatarId >= 0 && other.SpawnRoles) other.Peer.Send(unregisterBuilder);
            if (other.SpawnRoles)
                foreach (int role in gone.ClassAvatars.Values)
                    other.Peer.Send(new EventData((byte)EventCode.UnregisterWorldObject) { Parameters = { [(byte)ParameterKey.WorldObjectID] = role } });
            other.Peer.Send(leave);
        }

        Close(entry);
    }

    void Close(Entry entry)
    {
        Session session = entry.Session;
        if (session.Players.Count > 0 || session.WorldId is not int id) return;

        session.SaveIfChanged(force: true);
        lock (_sync) _worlds.Remove((id, session.Play, entry.Client));
        Console.WriteLine($"world {id} {(session.Play ? "play" : "edit")} closed");
    }

    static void ReleaseEverything(Session session, Player gone)
    {
        var events = new List<EventData>();

        int[] avatars = [gone.AvatarId, .. gone.ClassAvatars.Values];
        foreach (int avatarId in avatars)
        {
            if (session.World.Find(avatarId) is not { } avatar || avatar.ParentId == session.World.RootId) continue;
            int rootId = session.World.RootId;
            session.World.Modify(avatarId, obj =>
            {
                obj.ParentId = rootId;
                obj.SetRuntime("seat", PackedType.Int32, -1);
            });
            events.Add(new EventData((byte)EventCode.DetachWorldObjectFromVehicle)
            {
                Parameters = { [(byte)ParameterKey.WorldObjectID] = avatarId },
            });
        }

        var own = avatars.SelectMany(avatarId => session.World.Subtree(avatarId)).Select(obj => obj.Id).ToHashSet();
        if (gone.BuildAvatarId >= 0) own.UnionWith(session.World.Subtree(gone.BuildAvatarId).Select(obj => obj.Id));
        foreach (WorldObject obj in session.World.ToSnapshot().Objects)
        {
            if (obj.Owner != gone.Actor || own.Contains(obj.Id)) continue;

            session.World.Modify(obj.Id, o => o.Owner = null);
            events.Add(new EventData((byte)EventCode.TransferOwnership)
            {
                Parameters =
                {
                    [(byte)ParameterKey.WorldObjectID] = obj.Id,
                    [(byte)ParameterKey.OwnerActorNr] = 0,
                    [(byte)ParameterKey.FinalizeTransform] = false,
                },
            });
        }

        foreach (Player other in session.Players)
            if (other != gone)
                foreach (EventData evt in events)
                    other.Peer.Send(evt);

        if (events.Count > 0)
            Console.WriteLine($"actor {gone.Actor}: released {events.Count} objects");
    }

    public bool DeleteWorld(int id)
    {
        lock (_sync)
        {
            if (_worlds.Keys.Any(key => key.Id == id) || !Stores.Worlds.Delete(id)) return false;
        }

        Stores.Images.DeleteImage(0, id);
        Console.WriteLine($"world {id} deleted");
        return true;
    }

    public byte[]? ExportWorld(int id)
    {
        foreach (Session session in Worlds())
            if (session.WorldId == id) session.SaveIfChanged();

        if (Stores.Worlds.World(id) is not { } stored) return null;
        var meta = new JsonObject
        {
            ["GameTitle"] = stored.Name,
            ["SavedAt"] = DateTime.UtcNow.ToString("O"),
            ["Kgmexporter"] = "openkogama-server",
        };
        return KgmapFile.Pack(meta, stored.Data);
    }

    public void SaveAll(bool force = false)
    {
        foreach (Session session in Worlds())
            session.SaveIfChanged(force);
    }

    public void Tick()
    {
        foreach (Session session in Worlds())
        {
            string world = $"world {session.WorldId}";
            TickErrors.Guard(world + " logic", session.Logic.Tick);
            TickErrors.Guard(world + " spins", () => Spins.Tick(session));
            TickErrors.Guard(world + " experience", () => Experience.Tick(session));
            TickErrors.Guard(world + " game passes", session.GamePasses.Tick);
            TickErrors.Guard(world + " npcs", session.TickNpcs);
            TickErrors.Guard(world + " announcements", session.TickAnnouncements);
        }
        TickErrors.Guard("plugins", Plugins.PluginHost.Tick);
    }

    public IReadOnlyList<Session> Sessions() => Worlds();

    List<Session> Worlds()
    {
        lock (_sync) return [.. _worlds.Values.Select(entry => entry.Session)];
    }

    Entry? Assign(PhotonPeer peer, OperationRequest request)
    {
        int mode = request.Parameters.TryGetValue((byte)ParameterKey.GameMode, out object? value) ? Convert.ToInt32(value) : (int)GameMode.Edit;
        if (mode == (int)GameMode.CharacterEditor)
        {
            Console.WriteLine($"peer {peer.Id}: character editor");
            int parsed = SessionTokens.Find(request.Parameters.GetValueOrDefault((byte)ParameterKey.Token) as string).Profile;
            int profile = parsed > 0 ? parsed : 1;
            return Create(Session.CharacterEditor(peer.Id, profile), editor: true);
        }

        int id = request.Parameters.TryGetValue((byte)ParameterKey.PlanetID, out object? planet) ? Convert.ToInt32(planet) : 0;
        if (id <= 0) id = Stores.Worlds.List().FirstOrDefault()?.Id ?? 0;

        bool play = mode == (int)GameMode.Play;
        string client = mixedClients ? "" : (peer.Translator as LegacyTranslator)?.Version ?? "";
        if (_worlds.TryGetValue((id, play, client), out Entry? open)) return open;
        int requester = SessionTokens.Find(request.Parameters.GetValueOrDefault((byte)ParameterKey.Token) as string).Profile;
        if (!play && EditLocked(id) && !TakeOver(id, peer, requester))
        {
            Console.WriteLine($"peer {peer.Id}: world {id} is already edited by another client version");
            return null;
        }
        if (Session.Open(id, play) is not { } session) return null;
        session.Primary = client == "";

        Console.WriteLine($"world {id} {(play ? "play" : "edit")} opened{(client == "" ? "" : $" for {client}")}");
        return _worlds[(id, play, client)] = Create(session, editor: false, client);
    }

    bool EditLocked(int id) => _worlds.Keys.Any(key => key.Id == id && !key.Play);

    bool TakeOver(int id, PhotonPeer peer, int profile)
    {
        List<Entry> rivals = [.. _worlds.Where(pair => pair.Key.Id == id && !pair.Key.Play).Select(pair => pair.Value)];
        List<Player> editors = [.. rivals.SelectMany(rival => rival.Session.Players)];
        if (editors.Any(editor => editor.ProfileId != profile || !editor.Peer.EndPoint.Address.Equals(peer.EndPoint.Address))) return false;

        foreach (Player editor in editors)
        {
            Console.WriteLine($"peer {peer.Id}: takes world {id} over from peer {editor.Peer.Id}, the same player on another client version");
            editor.Peer.Disconnect();
        }
        foreach (Entry rival in rivals) Close(rival);
        return !EditLocked(id);
    }

    Entry Create(Session session, bool editor, string client = "")
    {
        session.Begin(() => server.Now);
        return new Entry(session, new OperationRouter(server, session, Console.WriteLine), editor, client);
    }
}
