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
    sealed record Entry(Session Session, OperationRouter Router, bool Editor, string Client = "", Router2012? Classic = null);

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

        if (peer.Translator is null && request.OperationCode == (byte)OperationCode.Join && ClientProtocols.Detect(request) is { } detected)
        {
            peer.Translator = detected;
            Console.WriteLine($"peer {peer.Id}: client {detected.Version}");
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

        if (entry is null)
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
                entry = Assign2012(request);
                if (entry is not null) _peers[peer] = entry;
            }
        }

        if (entry?.Classic is null)
        {
            Console.WriteLine($"peer {peer.Id}: 2012 op {request.OperationCode} rejected, not in a world");
            peer.Send(new OperationResponse(request) { ReturnCode = -2 });
            return;
        }

        entry.Classic.Handle(peer, request);
    }

    Entry? Assign2012(OperationRequest join)
    {
        const string Client = "2012";
        if (!int.TryParse(join[(byte)Key2012.PlanetName] as string, out int id)) return null;

        bool play = join[(byte)Key2012.JoinEditMode] is not true;
        if (_worlds.TryGetValue((id, play, Client), out Entry? open)) return open;
        if (!play && _worlds.Keys.Any(key => key.Id == id && !key.Play)) return null;
        if (Session.Open(id, play) is not { } session) return null;

        Console.WriteLine($"world {id} {(play ? "play" : "edit")} opened for {Client}");
        return _worlds[(id, play, Client)] = Create(session, editor: false, Client) with { Classic = new Router2012(session) };
    }

    public void Disconnect(PhotonPeer peer)
    {
        Entry? entry;
        lock (_sync)
            if (!_peers.Remove(peer, out entry)) return;

        Session session = entry.Session;
        Player? gone = session.For(peer);
        if (gone is null) return;

        if (entry.Editor)
        {
            session.SaveEditedAvatar(gone);
            return;
        }

        if (entry.Classic is not null)
        {
            session.Remove(gone);
            entry.Classic.Left(gone);
            Close(entry);
            return;
        }

        ReleaseEverything(session, gone);
        session.Remove(gone);
        session.Round.Stats.RemoveActor(gone.Actor);

        foreach (int trigger in session.Triggers.ExitAll(gone.Actor))
            TriggerBox.Send(session, trigger, gone.Actor, pressed: false);
        session.Logic.Evaluate();

        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.AvatarId },
        };
        var leave = new EventData((byte)EventCode.Leave)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = gone.Actor },
        };
        foreach (Player other in session.Players)
        {
            other.Peer.Send(unregister);
            other.Peer.Send(leave);
        }

        Close(entry);
    }

    void Close(Entry entry)
    {
        Session session = entry.Session;
        if (session.Players.Count > 0 || session.WorldId is not int id) return;

        session.SaveIfChanged();
        lock (_sync) _worlds.Remove((id, session.Play, entry.Client));
        Console.WriteLine($"world {id} {(session.Play ? "play" : "edit")} closed");
    }

    static void ReleaseEverything(Session session, Player gone)
    {
        var events = new List<EventData>();

        if (session.World.Find(gone.AvatarId) is { } avatar && avatar.ParentId != session.World.RootId)
        {
            int rootId = session.World.RootId;
            session.World.Modify(gone.AvatarId, obj =>
            {
                obj.ParentId = rootId;
                obj.SetRuntime("seat", PackedType.Int32, -1);
            });
            events.Add(new EventData((byte)EventCode.DetachWorldObjectFromVehicle)
            {
                Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.AvatarId },
            });
        }

        var own = session.World.Subtree(gone.AvatarId).Select(obj => obj.Id).ToHashSet();
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

    public void SaveAll()
    {
        foreach (Session session in Worlds())
            session.SaveIfChanged();
    }

    public void Tick()
    {
        foreach (Session session in Worlds())
            session.Logic.Tick();
    }

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
            int profile = int.TryParse(request.Parameters.GetValueOrDefault((byte)ParameterKey.Token) as string, out int parsed) && parsed > 0 ? parsed : 1;
            return Create(Session.CharacterEditor(peer.Id, profile), editor: true);
        }

        int id = request.Parameters.TryGetValue((byte)ParameterKey.PlanetID, out object? planet) ? Convert.ToInt32(planet) : 0;
        if (id <= 0) id = Stores.Worlds.List().FirstOrDefault()?.Id ?? 0;

        bool play = mode == (int)GameMode.Play;
        string client = mixedClients ? "" : (peer.Translator as LegacyTranslator)?.Version ?? "";
        if (_worlds.TryGetValue((id, play, client), out Entry? open)) return open;
        if (!play && _worlds.Keys.Any(key => key.Id == id && !key.Play))
        {
            Console.WriteLine($"peer {peer.Id}: world {id} is already edited by another client version");
            return null;
        }
        if (Session.Open(id, play) is not { } session) return null;

        Console.WriteLine($"world {id} {(play ? "play" : "edit")} opened{(client == "" ? "" : $" for {client}")}");
        return _worlds[(id, play, client)] = Create(session, editor: false, client);
    }

    Entry Create(Session session, bool editor, string client = "")
    {
        session.Begin(() => server.Now);
        return new Entry(session, new OperationRouter(server, session, Console.WriteLine), editor, client);
    }
}
