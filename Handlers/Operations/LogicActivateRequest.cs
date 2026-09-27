using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class LogicActivateRequest(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.LogicActivateRequest;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        bool activate = request[(byte)ParameterKey.IsFiring] is true;
        if (session.For(peer) is not { } player || session.World.Find(objectId) is not { } obj) return;
        if (obj.Type is not (WorldObjectType.ShootableButton or WorldObjectType.UseLever)) return;

        if (!session.Triggers.Switch(objectId, activate, Logic.StartsOn(obj))) return;
        session.Logic.Signal(objectId, player.Actor, activate);
        if (activate && obj.Type == WorldObjectType.ShootableButton) session.Logic.ReleaseLater(obj);
        session.Logic.Evaluate();
    }
}
