namespace OpenKogama.Kogama.Protocols;

// Runtime fields that some client generations type differently from the server.
public static class RuntimeTypes
{
    static readonly (string Key, Version From, Version Before, string Client, string Server)[] Rules =
    [
        ("maxHealth", new Version(2, 6), new Version(2, 8), "float", "int"),
    ];

    public static Dictionary<string, ProtocolTable.StateType> For(string version)
    {
        if (!Version.TryParse(version, out Version? parsed)) return [];
        return Rules.Where(rule => parsed >= rule.From && parsed < rule.Before)
            .ToDictionary(rule => rule.Key, rule => new ProtocolTable.StateType(rule.Client, rule.Server));
    }
}
