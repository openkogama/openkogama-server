using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Ban(Session session) : IOperationHandler
{
    static readonly string[] Cheats = ["SpeedHack", "MemTampering", "TextureTampering"];

    public byte Code => (byte)OperationCode.Ban;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int cheat = Convert.ToInt32(request[(byte)ParameterKey.CheatType]);
        string name = cheat >= 0 && cheat < Cheats.Length ? Cheats[cheat] : cheat.ToString();
        Console.WriteLine($"peer {peer.Id} profile {session.For(peer)?.ProfileId}: client anticheat reported {name}");
        peer.Send(new OperationResponse(request));
    }
}
