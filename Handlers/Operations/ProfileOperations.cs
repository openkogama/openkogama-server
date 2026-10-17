using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class AdminOperation(Session session) : IOperationHandler
{
    const byte Kick = 0;
    const byte Ban = 1;
    const byte Expel = 2;
    const short Refused = -1;

    public byte Code => (byte)OperationCode.AdminOperation;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? admin = session.For(peer);
        byte type = request[(byte)ParameterKey.OperationType] is { } kind ? Convert.ToByte(kind) : byte.MaxValue;
        int target = request[(byte)ParameterKey.ProfileID] is { } profile ? Convert.ToInt32(profile) : 0;
        Player? player = admin is not null && Plugins.PluginHost.Admin(admin) && type is Kick or Ban or Expel
            ? session.Players.FirstOrDefault(other => other.ProfileId == target && other != admin && !Plugins.PluginHost.Admin(other))
            : null;

        peer.Send(new OperationResponse(request) { ReturnCode = player is null ? Refused : (short)0 });
        if (player is null) return;

        string reason = request[(byte)ParameterKey.GameMsgData] as string ?? "";
        if (type == Ban)
        {
            int hours = Math.Clamp(request[(byte)ParameterKey.Amount] is { } amount ? Convert.ToInt32(amount) : 0, 1, Bans.MaxHours);
            Bans.Add(player, hours, reason);
            Console.WriteLine($"peer {peer.Id}: admin {admin!.Username} banned {player.Username} for {hours} h: {reason}");
        }
        else
        {
            Console.WriteLine($"peer {peer.Id}: admin {admin!.Username} kicked {player.Username}: {reason}");
        }
        player.Peer.Disconnect();
    }
}

public sealed class OwnerOperation(Session session) : IOperationHandler
{
    const byte RevokeEditRights = 0;
    const short Refused = -1;

    public byte Code => (byte)OperationCode.OwnerOperation;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? owner = session.For(peer);
        int target = Convert.ToInt32(request[(byte)ParameterKey.ProfileID]);
        bool allowed = owner is not null && !session.Play && session.OwnershipOf(owner) == PlanetOwnership.Owner
            && Convert.ToByte(request[(byte)ParameterKey.OperationType]) == RevokeEditRights;
        Player? editor = allowed ? session.Players.FirstOrDefault(player => player.ProfileId == target && player != owner) : null;

        peer.Send(new OperationResponse(request) { ReturnCode = editor is null ? Refused : (short)0 });
        if (editor is null) return;

        Console.WriteLine($"peer {peer.Id}: owner revoked edit rights of profile {target}");
        editor.Peer.Disconnect();
    }
}
