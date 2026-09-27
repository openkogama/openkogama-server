using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class GetNextGameBatch(Session session) : IOperationHandler
{
    public static void SendAdded(Session session, int actor, Snapshot added)
    {
        byte[]? plain = null, withState = null;
        foreach (Player player in session.Players)
        {
            if (!player.Knows(added)) continue;
            byte[] data = player.LinkState
                ? withState ??= WorldSerializer.Write(added, linkState: true)
                : plain ??= WorldSerializer.Write(added);
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

        if (!_worldSent.Add(peer.Id)) return;

        Player? me = session.For(peer);
        if (me is null) return;

        me.InWorld = true;
        if (peer.Translator is null) Console.WriteLine($"peer {peer.Id}: client {me.ClientVersion}");
        Snapshot snapshot = session.World.ToSnapshot();
        if (peer.Translator is null)
        {
            snapshot = LegacyWorld.KnownItems(snapshot, Kogama.Protocols.ProtocolTable.For(me.ClientVersion), out int dropped);
            if (dropped > 0) Console.WriteLine($"peer {peer.Id}: hid {dropped} unsupported or broken objects");
        }
        byte[] world = WorldSerializer.Write(snapshot, linkState: me.LinkState);

        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = me.Actor,
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

        byte[] addition = WorldSerializer.Write(session.World.SubtreeSnapshot(me.AvatarId));

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
                    [(byte)ParameterKey.Data] = addition,
                    [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
                },
            });
        }

        Console.WriteLine($"peer {peer.Id}: world sent, players now {session.Players.Count}");
    }
}
