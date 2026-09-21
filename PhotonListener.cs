using Microsoft.Extensions.Hosting;
using OpenKogama.Photon;
using OpenKogama.Handlers;

public class PhotonListener : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PhotonServer server = new PhotonServer(5055);
        server.Log = Console.WriteLine;
        server.Connected += peer => Console.WriteLine($"peer {peer.Id}: photon init done");
        server.Disconnected += (peer, reason) => Console.WriteLine($"peer {peer.Id}: gone ({reason})");

        OperationRouter router = new OperationRouter(Console.WriteLine);
        server.Operation = router.Handle;

        await server.RunAsync(stoppingToken);
    }
}