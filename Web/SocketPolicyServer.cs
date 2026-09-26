using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenKogama.Web;

public sealed class SocketPolicyServer(int port = 843)
{
    static readonly byte[] Policy = Encoding.ASCII.GetBytes(
        "<?xml version=\"1.0\"?><cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"*\"/></cross-domain-policy>\0");

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            Console.WriteLine($"policy {port}: {e.Message}");
            return;
        }

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    static async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[256];
                var request = new StringBuilder();

                while (!request.ToString().Contains('\0') && request.Length < 1024)
                {
                    int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    if (read == 0) break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                if (request.ToString().StartsWith("<policy-file-request/>"))
                    await stream.WriteAsync(Policy);
            }
            catch (Exception)
            {
            }
        }
    }
}
