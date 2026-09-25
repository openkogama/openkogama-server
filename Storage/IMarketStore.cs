namespace OpenKogama.Storage;

public enum ListingKind
{
    Item = 0,
    Avatar = 1,
}

public sealed record Listing(int Id, ListingKind Kind, int Owner, int Source, string Name, string Description, int Category, int Price, byte[] Data);

public interface IMarketStore
{
    List<Listing> List(ListingKind kind);
    Listing? Find(int id);
    Listing? FindBySource(ListingKind kind, int source);
    int Put(ListingKind kind, int owner, int source, string name, string description, int category, int price, byte[] data);
    bool Remove(ListingKind kind, int owner, int source);
}
