namespace OpenKogama.Storage;

public sealed record StoredWorld(int Id, string Name, int Owner, byte[] Data);

public sealed record WorldInfo(int Id, string Name, int Owner, string SavedAt, string? PublishedAt, string? PlayedAt);

public interface IWorldStore
{
    List<WorldInfo> List();
    StoredWorld? World(int id);
    byte[]? Published(int id);
    int Create(string name, int owner, byte[] data);
    void SaveWorld(StoredWorld world);
    void Publish(int id, byte[] data);
    void MarkPlayed(int id);
    bool Rename(int id, string name);
    bool Delete(int id);
}
