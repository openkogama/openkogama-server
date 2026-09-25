using OpenKogama.Kogama;

namespace OpenKogama.Storage;

public sealed record Friendship(int Id, int Profile, int Friend, FriendStatus Status);

public interface IFriendStore
{
    List<Friendship> Friends(int profile);
    Friendship? Find(int id);
    Friendship? Between(int profile, int other);
    Friendship Request(int profile, int friend);
    void SetStatus(int id, FriendStatus status);
    void Remove(int id);
}
