using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public static class ClientProtocols
{
    public const string ServerVersion = "1.25.12.280";
    const byte GameId = 255;
    public const byte Handshake = 248;
    public const byte HandshakeEvent = 252;
    public const byte HandshakeData = 245;
    static readonly string[] Legacy = ["1.9.0.1", "1.8.15.2", "1.8.8.4"];

    public static string? Hint(OperationRequest join) => join[GameId] as string ?? (Reported(join) is string reported ? WithEngine(join, reported) : null);

    static string WithEngine(OperationRequest join, string version)
    {
        foreach (string table in ProtocolTable.Candidates(version))
            if (ProtocolTable.For(table).ParameterKeys.TryGetValue("ProfileToken", out int key)
                && int.TryParse(join[(byte)key] as string, out int profile)
                && Game.ClientEngines.Of(profile) is string unity)
                return version + "@" + unity;
        return version;
    }

    public static string NativeVersion(OperationRequest join) => Native(join) ?? ServerVersion;

    public static OperationRemap? Remap(OperationRequest join) => Native(join) is string version ? OperationRemap.For(version) : null;

    public static string? Native(string? hint) => hint is null ? null : Native(hint, _ => true);

    static string? Native(OperationRequest join) => Hint(join) is string hint ? Native(hint, table => SentBy(join, table)) : null;

    static string? Native(string hint, Func<string, bool> matches)
    {
        List<string> tables = [.. ProtocolTable.Candidates(hint).Where(table => !ProtocolTable.IsLegacy(table))];
        return tables.FirstOrDefault(matches) ?? tables.FirstOrDefault();
    }

    static bool SentBy(OperationRequest join, string table) =>
        Sends(join, ProtocolTable.For(table), "ProfileToken") && Sends(join, ProtocolTable.For(table), "Version");

    static bool Sends(OperationRequest join, ProtocolTable table, string name) =>
        !table.ParameterKeys.TryGetValue(name, out int key) || join[(byte)key] is string;

    static string? Reported(OperationRequest join)
    {
        foreach ((byte key, object? value) in join.Parameters)
        {
            if (value is not string text) continue;
            string[] parts = text.Split('.');
            foreach (string version in parts.Length == 4 ? [text, string.Join('.', parts[..3])] : new[] { text })
                if (ProtocolTable.Candidates(version).Any(table => ProtocolTable.For(table).ParameterKeys.GetValueOrDefault("Version", -1) == key))
                    return version;
        }
        return null;
    }

    public static LegacyTranslator? Detect(OperationRequest join)
    {
        ProtocolTable server = ProtocolTable.For(ServerVersion);
        if (Hint(join) is string hint && ProtocolTable.Resolve(hint) is string table)
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
