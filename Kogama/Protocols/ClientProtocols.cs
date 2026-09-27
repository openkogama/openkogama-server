using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public static class ClientProtocols
{
    public const string ServerVersion = "1.25.12.280";
    const byte GameId = 255;
    static readonly string[] Legacy = ["1.9.0.1", "1.8.15.2", "1.8.8.4"];

    public static string? Hint(OperationRequest join) => join[GameId] as string;

    public static string NativeVersion(OperationRequest join) => Native(join) ?? ServerVersion;

    public static OperationRemap? Remap(OperationRequest join) => Native(join) is string version ? OperationRemap.For(version) : null;

    public static string? Native(string? hint) => hint is null ? null : Native(hint, _ => true);

    static string? Native(OperationRequest join) => join[GameId] is string hint ? Native(hint, table => SentBy(join, table)) : null;

    static string? Native(string hint, Func<string, bool> matches)
    {
        List<string> tables = [.. ProtocolTable.Candidates(hint).Where(table => !ProtocolTable.IsLegacy(table))];
        return tables.FirstOrDefault(matches) ?? tables.FirstOrDefault();
    }

    static bool SentBy(OperationRequest join, string table) =>
        !ProtocolTable.For(table).ParameterKeys.TryGetValue("ProfileToken", out int key) || join[(byte)key] is string;

    public static LegacyTranslator? Detect(OperationRequest join)
    {
        ProtocolTable server = ProtocolTable.For(ServerVersion);
        if (join[GameId] is string hint && ProtocolTable.Resolve(hint) is string table)
            return ProtocolTable.IsLegacy(table) ? new LegacyTranslator(ProtocolTable.For(table), server) : null;

        if (join[(byte)server.ParameterKeys["ProfileToken"]] is string) return null;

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
