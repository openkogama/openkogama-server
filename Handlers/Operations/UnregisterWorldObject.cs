using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class UnregisterWorldObject(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UnregisterWorldObject;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);

        WorldObject? obj = session.World.Find(objectId);
        if (obj is null || obj.Type == WorldObjectType.Avatar)
        {
            Console.WriteLine($"peer {peer.Id}: cannot delete object {objectId}");
            return;
        }

        List<(int Id, bool ObjectLink)> links = session.World.LinksAround(objectId);
        List<int> removedPrototypes = session.World.RemoveTree(objectId);
        session.World.MarkChanged();
        session.Logic.Evaluate();
        session.Teams.Update();
        Broadcast(session, objectId, removedPrototypes, peer, links);

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = objectId },
        });
        Plugins.PluginHost.Removed(session, objectId, session.For(peer));
    }

    public static void Broadcast(Session session, int objectId, List<int> removedPrototypes, PhotonPeer? except, List<(int Id, bool ObjectLink)> links)
    {
        foreach ((int linkId, bool objectLink) in links)
        {
            var linkGone = new EventData((byte)(objectLink ? EventCode.RemoveObjectLink : EventCode.RemoveLink))
            {
                Parameters = { [(byte)ParameterKey.LinkID] = linkId },
            };
            foreach (Player player in session.Players)
                player.Peer.Send(linkGone);
        }

        foreach (int prototypeId in removedPrototypes)
        {
            var prototypeGone = new EventData((byte)EventCode.UnregisterPrototype)
            {
                Parameters = { [(byte)ParameterKey.WorldInventoryID] = prototypeId },
            };
            foreach (Player player in session.Players)
                player.Peer.Send(prototypeGone);
        }

        var objectGone = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = objectId },
        };
        foreach (Player player in session.Players)
            if (player.Peer != except)
                player.Peer.Send(objectGone);
    }
}
