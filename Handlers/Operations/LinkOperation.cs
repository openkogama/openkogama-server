using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class LinkOperation(Session session, bool objectLink, bool adding) : IOperationHandler
{
    public byte Code => (byte)((objectLink, adding) switch
    {
        (false, true) => OperationCode.AddLink,
        (false, false) => OperationCode.RemoveLink,
        (true, true) => OperationCode.AddObjectLink,
        (true, false) => OperationCode.RemoveObjectLink,
    });

    EventCode Event => (objectLink, adding) switch
    {
        (false, true) => EventCode.AddLink,
        (false, false) => EventCode.RemoveLink,
        (true, true) => EventCode.AddObjectLink,
        (true, false) => EventCode.RemoveObjectLink,
    };

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)Event);
        var response = new OperationResponse(request);
        Link link;

        if (adding)
        {
            int from = Convert.ToInt32(request[(byte)ParameterKey.LinkFromID]);
            int to = Convert.ToInt32(request[(byte)ParameterKey.LinkToID]);
            link = session.World.AddLink(from, to, objectLink);

            evt.Parameters[(byte)ParameterKey.LinkID] = link.Id;
            evt.Parameters[(byte)ParameterKey.LinkFromID] = link.From;
            evt.Parameters[(byte)ParameterKey.LinkToID] = link.To;
            response.Parameters[(byte)ParameterKey.LinkID] = link.Id;
        }
        else
        {
            int id = Convert.ToInt32(request[(byte)ParameterKey.LinkID]);
            if (session.World.FindLink(id, objectLink) is not { } removed || !session.World.RemoveLink(id, objectLink))
            {
                Console.WriteLine($"peer {peer.Id}: no link {id} to remove");
                return;
            }
            link = removed;
            evt.Parameters[(byte)ParameterKey.LinkID] = id;
        }

        session.World.MarkChanged();
        session.Logic.Evaluate();

        foreach (Player player in session.Players)
            if (player.Peer != peer || player.LinkEvents)
                player.Peer.Send(evt);

        peer.Send(response);
        if (adding) Plugins.PluginHost.LinkAdded(session, link, objectLink, session.For(peer));
        else Plugins.PluginHost.LinkRemoved(session, link, objectLink, session.For(peer));
    }
}
