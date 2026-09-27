using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public sealed class OperationRemap(ProtocolTable client) : IMessageTranslator
{
    static readonly Dictionary<string, int> Server = ServerOperations();

    readonly CodeMap _in = new(client.OperationCodes, Server);
    readonly CodeMap _out = new(Server, client.OperationCodes);

    public string Version => client.Version;

    public PhotonPeer? Peer { get; set; }

    public bool Pushing { get; set; }

    public static OperationRemap? For(string version)
    {
        ProtocolTable table = ProtocolTable.For(version);
        bool renumbered = table.OperationCodes.Any(pair => Server.TryGetValue(pair.Key, out int code) && code != pair.Value);
        return renumbered ? new OperationRemap(table) : null;
    }

    public string ClientOperation(byte code) => _in.Name(code);

    public OperationRequest? Incoming(OperationRequest request) =>
        _in.Map(request.OperationCode) is int code
            ? new OperationRequest { OperationCode = (byte)code, Parameters = request.Parameters }
            : null;

    public OperationResponse? Outgoing(OperationResponse response)
    {
        if (Pushing && Peer is not null && client.EventCodes.TryGetValue(_out.Name(response.OperationCode), out int pushed))
        {
            Peer.Send(new EventData((byte)pushed) { Parameters = response.Parameters });
            return null;
        }

        return _out.Map(response.OperationCode) is int code
            ? new OperationResponse((byte)code) { ReturnCode = response.ReturnCode, DebugMessage = response.DebugMessage, Parameters = response.Parameters }
            : null;
    }

    public EventData? Outgoing(EventData data) => data;

    static Dictionary<string, int> ServerOperations()
    {
        var operations = new Dictionary<string, int>(ProtocolTable.For(ClientProtocols.ServerVersion).OperationCodes);
        foreach (OperationCode code in Enum.GetValues<OperationCode>())
            if (!operations.ContainsValue((int)code))
                operations.TryAdd(code.ToString(), (int)code);
        return operations;
    }
}
