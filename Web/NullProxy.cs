using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenKogama.Web;

public sealed class NullProxy(int port)
{
    static readonly byte[] Ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

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
                var buffer = new byte[8192];
                var request = new StringBuilder();

                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    if (read == 0) break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                await stream.WriteAsync(Ok);
            }
            catch (Exception)
            {
            }
        }
    }
}
