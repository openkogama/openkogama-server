using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class AdminOperation : IOperationHandler
{
    const short NotAdmin = -1;

    public byte Code => (byte)OperationCode.AdminOperation;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request) { ReturnCode = NotAdmin });
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
