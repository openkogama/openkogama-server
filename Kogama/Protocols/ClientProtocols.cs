using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public static class ClientProtocols
{
    const string ServerVersion = "1.25.12.280";
    static readonly string[] Legacy = ["1.9.0.1"];

    public static LegacyTranslator? Detect(OperationRequest join)
    {
        ProtocolTable server = ProtocolTable.For(ServerVersion);
        if (join[(byte)server.ParameterKeys["ProfileToken"]] is string) return null;

        foreach (string version in Legacy)
        {
            ProtocolTable client = ProtocolTable.For(version);
            if (client.ParameterKeys.TryGetValue("ProfileToken", out int key) && join[(byte)key] is string)
                return new LegacyTranslator(client, server);
        }

        return null;
    }
}
