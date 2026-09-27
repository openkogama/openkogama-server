using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public static class ClientProtocols
{
    public const string ServerVersion = "1.25.12.280";
    const byte GameId = 255;
    static readonly string[] Legacy = ["1.9.0.1", "1.8.15.2", "1.8.8.4"];

    public static string NativeVersion(OperationRequest join) => Native(join[GameId] as string) ?? ServerVersion;

    public static string? Native(string? hint) =>
        hint is not null && ProtocolTable.Resolve(hint) is string table && !ProtocolTable.IsLegacy(table) ? table : null;

    public static LegacyTranslator? Detect(OperationRequest join)
    {
        ProtocolTable server = ProtocolTable.For(ServerVersion);
        if (join[(byte)server.ParameterKeys["ProfileToken"]] is string) return null;

        if (join[GameId] is string hint && ProtocolTable.Resolve(hint) is string table && ProtocolTable.IsLegacy(table))
            return new LegacyTranslator(ProtocolTable.For(table), server);

        foreach (string version in Legacy)
        {
            ProtocolTable client = ProtocolTable.For(version);
            bool matches = client.ParameterKeys.TryGetValue("ProfileToken", out int key)
                ? join[(byte)key] is string
                : join[(byte)client.ParameterKeys["ProfileID"]] is int;
            if (matches) return new LegacyTranslator(client, server);
        }

        return null;
    }
}
