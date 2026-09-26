using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AttachWorldObjectToSeat(Session session) : IOperationHandler
{
    const byte Driver = 0;
    const byte Vehicle = 4;

    public byte Code => (byte)OperationCode.AttachWorldObjectToSeat;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var ids = PhotonValues.Table(request[(byte)ParameterKey.WorldObjectIDs]);
        int driverId = Convert.ToInt32(ids[Driver]);
        int vehicleId = Convert.ToInt32(ids[Vehicle]);
        byte seat = Convert.ToByte(request[(byte)ParameterKey.SeatID]);
        Player? player = session.For(peer);

        if (player is null || session.World.Find(vehicleId) is null)
        {
            Console.WriteLine($"peer {peer.Id}: cannot sit in {vehicleId}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        foreach (WorldObject part in session.World.Subtree(vehicleId))
            session.World.Modify(part.Id, obj => obj.Owner = player.Actor);
        session.World.Modify(driverId, obj =>
        {
            obj.ParentId = vehicleId;
            obj.SetRuntime("seat", PackedType.Int32, (int)seat);
        });

        PhotonDictionary seated = PhotonDictionary.Untyped();
        seated.Add(Driver, driverId);
        seated.Add(Vehicle, vehicleId);

        var evt = new EventData((byte)EventCode.AttachWorldObjectToSeat)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = seated,
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.SeatID] = seat,
            },
        };
        foreach (Player other in session.Players)
            other.Peer.Send(evt);

        peer.Send(new OperationResponse(request));
    }
}
