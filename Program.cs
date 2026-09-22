using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Handlers;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Web;

var server = new PhotonServer(5055) { Log = Console.WriteLine };
var session = new Session();

server.Connected += peer => Console.WriteLine($"peer {peer.Id}: photon init done");
server.Disconnected += (peer, reason) =>
{
    Console.WriteLine($"peer {peer.Id}: gone ({reason})");

    Player? gone = session.For(peer);
    session.Remove(peer);
    if (gone is null) return;

    var evt = new EventData((byte)EventCode.UnregisterWorldObject)
    {
        Parameters = { [(byte)ParameterKey.WorldObjectID] = gone.AvatarId },
    };
    foreach (Player other in session.Players)
        other.Peer.Send(evt);
};

var router = new OperationRouter(server, session, Console.WriteLine);
server.Operation = router.Handle;

_ = server.RunAsync();
Console.WriteLine("udp 5055");

// run kogama with "kogama.exe kogamaPackage:aHR0cDovLzEyNy4wLjAuMTo4MDgwL3Nlc3Npb24="
var sessionData = new SessionData("127.0.0.1:5055", 1, 0, GameMode.Play, "en_US", false, "0");
var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

var web = new HttpServer("http://127.0.0.1:8080/") { SessionJson = json };
Console.WriteLine("http 8080");
web.Run();
