using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddPlanetToPlanet(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.AddPlanetToPlanet;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int planetId = Convert.ToInt32(request[(byte)ParameterKey.PlanetID]);
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);

        if (session.For(peer) is not Player player || Stores.Worlds.World(planetId) is not { } stored
            || GameWorld.FromData(stored.Name, null, [stored.Data]) is not { } source || source.Find(objectId) is null)
        {
            Console.WriteLine($"peer {peer.Id}: cannot copy object {objectId} from world {planetId}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Snapshot added = session.World.Insert(source.SubtreeSnapshot(objectId), session.World.RootId);
        session.World.MarkChanged();

        peer.Send(new OperationResponse(request));

        GetNextGameBatch.SendAdded(session, player.Actor, added);

        Console.WriteLine($"peer {peer.Id}: copied object {objectId} from world {planetId} as {added.Objects[0].Id}");
    }
}
