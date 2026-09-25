using OpenKogama.Game;
using OpenKogama.Handlers;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Hosting;

public sealed class SessionHost(PhotonServer server)
{
    sealed record Entry(Session Session, OperationRouter Router, bool Editor);

    readonly object _sync = new();
    readonly Dictionary<int, Entry> _worlds = [];
    readonly Dictionary<PhotonPeer, Entry> _peers = [];

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
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
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        entry.Router.Handle(peer, request);
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

        session.Remove(gone);
        session.Round.Stats.RemoveActor(gone.Actor);

        foreach (int trigger in session.Triggers.ExitAll(gone.Actor))
            TriggerBox.Send(session, trigger, gone.Actor, pressed: false);
        session.Logic.Evaluate();

        var evt = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.AvatarId },
        };
        foreach (Player other in session.Players)
            other.Peer.Send(evt);

        if (session.Players.Count == 0 && session.WorldId is int id)
        {
            session.SaveIfChanged();
            lock (_sync) _worlds.Remove(id);
            Console.WriteLine($"world {id} closed");
        }
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
        if (request.Parameters.TryGetValue((byte)ParameterKey.GameMode, out object? mode)
            && Convert.ToInt32(mode) == (int)GameMode.CharacterEditor)
        {
            Console.WriteLine($"peer {peer.Id}: character editor");
            return Create(Session.CharacterEditor(peer.Id), editor: true);
        }

        int id = request.Parameters.TryGetValue((byte)ParameterKey.PlanetID, out object? planet) ? Convert.ToInt32(planet) : 0;
        if (id <= 0) id = Stores.Worlds.List().FirstOrDefault()?.Id ?? 0;

        if (_worlds.TryGetValue(id, out Entry? open)) return open;
        if (Session.Open(id) is not { } session) return null;

        Console.WriteLine($"world {id} opened");
        return _worlds[id] = Create(session, editor: false);
    }

    Entry Create(Session session, bool editor)
    {
        session.Clock = () => server.Now;
        return new Entry(session, new OperationRouter(server, session, Console.WriteLine), editor);
    }
}
