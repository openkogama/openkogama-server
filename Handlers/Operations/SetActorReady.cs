using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetActorReady(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetActorReady;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? player = session.For(peer);
        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = player?.Actor ?? 0 },
        });
        if (player is null) return;

        session.Logic.Resync(player);
        Spins.Join(session, player);
        if (!player.Ready)
        {
            player.Ready = true;
            player.PlayingSince = DateTime.UtcNow;
            var ready = new EventData((byte)EventCode.SetActorReady)
            {
                Parameters = { [(byte)ParameterKey.ActorNr] = player.Actor },
            };
            foreach (Player other in session.Players)
                if (other != player && other.ReadyEvents && other.Saw(player.Actor))
                    other.Peer.Send(ready);
        }
        ProfileMeta.Send(player);
        session.GamePasses.Send(player);
        int xp = Leveling.XpOf(player.ProfileId);
        Leveling.PayLevelGold(player.ProfileId, xp);
        Experience.ShowLevelGold(player, xp);
        Plugins.PluginHost.Joined(session, player);
        Console.WriteLine($"peer {peer.Id}: actor ready");
    }
}
