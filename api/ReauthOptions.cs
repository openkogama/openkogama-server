namespace OpenKogama.Api;

public sealed class ReauthOptions
{
    public int? World { get; set; }
    public WorldMode? Mode { get; set; }
    public IDictionary<string, object?> Session { get; } = new Dictionary<string, object?>();
}
