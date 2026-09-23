using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Handlers;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Web;

var server = new PhotonServer(5055) { Log = Console.WriteLine };
var session = new Session();
session.Logic.Clock = () => server.Now;

server.Connected += peer => Console.WriteLine($"peer {peer.Id}: photon init done");
server.Disconnected += (peer, reason) =>
{
    Console.WriteLine($"peer {peer.Id}: gone ({reason})");

    Player? gone = session.For(peer);
    if (gone is null) return;
    session.Remove(gone);

    foreach (int trigger in session.Triggers.ExitAll(gone.Actor))
        TriggerBox.Send(session, trigger, gone.Actor, pressed: false);
    session.Logic.Evaluate();

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

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5000);
        session.SaveIfChanged();
    }
});
AppDomain.CurrentDomain.ProcessExit += (_, _) => session.SaveIfChanged();

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(50);
        session.Logic.Tick();
    }
});

// run kogama with "kogama.exe kogamaPackage:aHR0cDovLzEyNy4wLjAuMTo4MDgwL3Nlc3Npb24="
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string SessionJson(int profile) => JsonSerializer.Serialize(
    new SessionData("127.0.0.1:5055", profile, 0, GameMode.Edit, "en_US", false, "0",
        "http://127.0.0.1:8080/ping", "http://127.0.0.1:8080/disconnect"),
    jsonOptions);

var web = new HttpServer("http://127.0.0.1:8080/") { SessionJson = SessionJson };
Console.WriteLine("http 8080");
web.Run();
