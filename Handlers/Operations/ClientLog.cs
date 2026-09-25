using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class ClientLog : IOperationHandler
{
    static readonly string[] Types = ["error", "assert", "warning", "log", "exception"];

    public byte Code => (byte)OperationCode.ClientLog;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int type = request.Parameters.TryGetValue((byte)ParameterKey.LogType, out object? value) ? Convert.ToInt32(value) : 3;
        string kind = type >= 0 && type < Types.Length ? Types[type] : type.ToString();
        string message = request[(byte)ParameterKey.LogString] as string ?? "";
        string stack = (request[(byte)ParameterKey.StackTrace] as string ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        Console.WriteLine($"peer {peer.Id} client {kind}: {message}" + (stack.Length > 0 ? $" | {stack.Trim()}" : ""));
    }
}
