using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Legacy2012;

// A 2012 client in a shared world: events made for newer clients get the 2012 shape or are dropped.
public sealed class Translator2012 : IMessageTranslator
{
    static readonly ProtocolTable Table = ProtocolTable.For("2012");
    readonly HashSet<object> _raw = new(ReferenceEqualityComparer.Instance);

    public PhotonPeer? Peer { get; set; }
    public Game.Session? Session { get; set; }

    public void SendRaw(PhotonPeer peer, EventData data, bool reliable = true)
    {
        lock (_raw) _raw.Add(data);
        peer.Send(data, reliable: reliable);
    }

    public OperationResponse? Outgoing(OperationResponse response) => response;

    public EventData? Outgoing(EventData data)
    {
        lock (_raw)
            if (_raw.Remove(data)) return data;

        switch ((EventCode)data.Code)
        {
            case EventCode.Join:
                return new EventData((byte)Event2012.Join)
                {
                    Parameters =
                    {
                        [(byte)Key2012.Username] = data[(byte)ParameterKey.Username],
                        [(byte)Key2012.ProfileID] = data[(byte)ParameterKey.ProfileID],
                        [(byte)Key2012.ActorNr] = data[(byte)ParameterKey.ActorNr],
                    },
                };
            case EventCode.Leave:
                return new EventData((byte)Event2012.Leave) { Parameters = { [(byte)Key2012.ActorNr] = data[(byte)ParameterKey.ActorNr] } };
            case EventCode.UnregisterWorldObject:
                return new EventData((byte)Event2012.UnregisterWorldObject) { Parameters = { [(byte)Key2012.WorldObjectID] = data[(byte)ParameterKey.WorldObjectID] } };
            case EventCode.PostGameMsg:
                return Chat(data);
            case EventCode.UpdateWorldObject:
                return Moved(data);
            case EventCode.GetGameBatch:
                Added(data);
                return null;
            default:
                return null;
        }
    }

    static EventData? Chat(EventData data)
    {
        Dictionary<object, object?> table = PhotonValues.Table(data[(byte)ParameterKey.GameMsgData]);
        if (table.GetValueOrDefault((byte)Legacy.SendChatMsg.Text) is not string text) return null;
        int actor = table.GetValueOrDefault((byte)Legacy.SendChatMsg.Sender) is { } sender ? Convert.ToInt32(sender) : 0;
        return new EventData((byte)Event2012.ChatMsg) { Parameters = { [(byte)Key2012.ActorNr] = actor, [(byte)Key2012.ChatMsg] = text } };
    }

    EventData? Moved(EventData data)
    {
        if (data[(byte)ParameterKey.WorldObjectID] is not { } id) return null;
        int objectId = Convert.ToInt32(id);
        if (Session?.Players.Any(player => player.Peer.Protocol == PhotonProtocol.Protocol15 && player.AvatarId == objectId) == true) return null;
        var moved = new EventData((byte)Event2012.UpdateWorldObject) { Parameters = { [(byte)Key2012.WorldObjectID] = id } };
        if (data[(byte)ParameterKey.Timestamp] is { } stamp) moved.Parameters[(byte)Key2012.Timestamp] = stamp;
        foreach ((ParameterKey from, Key2012 to) in new[] { (ParameterKey.PosX, Key2012.PosX), (ParameterKey.PosY, Key2012.PosY), (ParameterKey.PosZ, Key2012.PosZ) })
            if (data[(byte)from] is { } value) moved.Parameters[(byte)to] = Convert.ToSingle(value);
        if (data[(byte)ParameterKey.ByteRotation] is byte[] { Length: 3 } bytes)
        {
            var (x, y, z, w) = QuaternionCompression.ToQuaternion(bytes);
            moved.Parameters[(byte)Key2012.RotX] = x;
            moved.Parameters[(byte)Key2012.RotY] = y;
            moved.Parameters[(byte)Key2012.RotZ] = z;
            moved.Parameters[(byte)Key2012.RotW] = w;
        }
        else
        {
            foreach ((ParameterKey from, Key2012 to) in new[] { (ParameterKey.RotX, Key2012.RotX), (ParameterKey.RotY, Key2012.RotY), (ParameterKey.RotZ, Key2012.RotZ), (ParameterKey.RotW, Key2012.RotW) })
                if (data[(byte)from] is { } value) moved.Parameters[(byte)to] = Convert.ToSingle(value);
        }
        return moved;
    }

    void Added(EventData data)
    {
        if (Peer is null || data[(byte)ParameterKey.QueryType] is not { } query || Convert.ToInt32(query) != (int)QueryType.AddToGameWorld
            || data[(byte)ParameterKey.Data] is not byte[] bytes) return;
        Snapshot snapshot;
        try
        {
            snapshot = WorldSerializer.Read(bytes);
        }
        catch (Exception error)
        {
            Console.WriteLine($"peer {Peer.Id}: 2012 client cannot read added objects, {error.Message}");
            return;
        }
        var built = Session?.Players.Where(player => player.Peer.Protocol == PhotonProtocol.Protocol15).Select(player => player.AvatarId).ToHashSet() ?? [];
        IReadOnlyList<WorldObject> objects = [.. snapshot.Objects.Where(obj => !built.Contains(obj.Id))];
        foreach (Dictionary<object, object?> described in WorldObjects2012.Describe(objects, Table))
        {
            var registered = new EventData((byte)Event2012.RegisterWorldObject);
            foreach ((object key, object? value) in described) registered.Parameters[(byte)key] = value;
            SendRaw(Peer, registered);
        }
    }
}
