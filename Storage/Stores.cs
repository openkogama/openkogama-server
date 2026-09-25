namespace OpenKogama.Storage;

public static class Stores
{
    public static IProfileStore Profiles { get; set; } = null!;
    public static IWorldStore Worlds { get; set; } = null!;
    public static IFriendStore Friends { get; set; } = null!;
    public static IImageStore Images { get; set; } = null!;
    public static IMarketStore Market { get; set; } = null!;
}
