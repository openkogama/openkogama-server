using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class GetNextGameBatch(Session session) : IOperationHandler
{
    public static void SendAdded(Session session, int actor, Snapshot added)
    {
        var formats = new Dictionary<(bool, bool), byte[]>();
        foreach (Player player in session.Players)
        {
            if (!player.Knows(added)) continue;
            (bool, bool) format = (player.LinkState, player.ObjectLinkState);
            if (!formats.TryGetValue(format, out byte[]? data))
                formats[format] = data = player.WorldData(added);
            player.Peer.Send(new EventData((byte)EventCode.GetGameBatch)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = actor,
                    [(byte)ParameterKey.Data] = data,
                    [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
                },
            });
        }
    }

    readonly HashSet<short> _worldSent = [];

    public byte Code => (byte)OperationCode.GetNextGameBatch;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int queryId = Convert.ToInt32(request[(byte)ParameterKey.QueryId]);

        peer.Send(new OperationResponse(request));

        SendWorld(peer, world =>
        {
            peer.Send(new EventData((byte)EventCode.GetGameBatch)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = session.For(peer)!.Actor,
                    [(byte)ParameterKey.Data] = world,
                    [(byte)ParameterKey.QueryType] = (byte)QueryType.GameWorld,
                    [(byte)ParameterKey.QueryId] = queryId,
                    [(byte)ParameterKey.QueryDataLeft] = false,
                },
            });

            peer.Send(new EventData((byte)EventCode.GameQueryReady)
            {
                Parameters = { [(byte)ParameterKey.QueryId] = queryId },
            });
        });
    }

    public void SendWorld(PhotonPeer peer, Action<byte[]> deliver)
    {
        if (!_worldSent.Add(peer.Id)) return;

        Player? me = session.For(peer);
        if (me is null) return;

        me.InWorld = true;
        bool native = peer.Translator is not Kogama.Protocols.LegacyTranslator;
        if (native) Console.WriteLine($"peer {peer.Id}: client {me.ClientVersion}");
        Snapshot snapshot = session.World.ToSnapshot();
        if (native)
        {
            snapshot = LegacyWorld.KnownItems(snapshot, Kogama.Protocols.ProtocolTable.For(me.ClientVersion), out int dropped);
            if (dropped > 0) Console.WriteLine($"peer {peer.Id}: hid {dropped} unsupported or broken objects");
        }
        deliver(me.WorldData(snapshot));

        Snapshot avatar = session.World.SubtreeSnapshot(me.AvatarId);

        foreach (Player other in session.Players)
        {
            if (other.Peer == peer) continue;

            other.Peer.Send(new EventData((byte)EventCode.Join)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ProfileID] = me.ProfileId,
                    [(byte)ParameterKey.ActorNr] = me.Actor,
                    [(byte)ParameterKey.Username] = me.Username,
                    [(byte)ParameterKey.RegionCode] = me.Region,
                },
            });

            other.Peer.Send(new EventData((byte)EventCode.GetGameBatch)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = me.Actor,
                    [(byte)ParameterKey.Data] = other.WorldData(avatar),
                    [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
                },
            });
        }

        Console.WriteLine($"peer {peer.Id}: world sent, players now {session.Players.Count}");
    }
}
