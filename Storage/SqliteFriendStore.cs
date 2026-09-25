using Microsoft.Data.Sqlite;
using OpenKogama.Kogama;

namespace OpenKogama.Storage;

public sealed class SqliteFriendStore(Database database) : IFriendStore
{
    public List<Friendship> Friends(int profile) =>
        Query("SELECT id, profile, friend, status FROM friends WHERE profile = $a OR friend = $a", ("$a", profile));

    public Friendship? Find(int id) =>
        Query("SELECT id, profile, friend, status FROM friends WHERE id = $id", ("$id", id)).FirstOrDefault();

    public Friendship? Between(int profile, int other) =>
        Query("""
            SELECT id, profile, friend, status FROM friends
            WHERE (profile = $a AND friend = $b) OR (profile = $b AND friend = $a)
            """, ("$a", profile), ("$b", other)).FirstOrDefault();

    public Friendship Request(int profile, int friend)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO friends (profile, friend, status) VALUES ($a, $b, $status) RETURNING id";
        command.Parameters.AddWithValue("$a", profile);
        command.Parameters.AddWithValue("$b", friend);
        command.Parameters.AddWithValue("$status", (int)FriendStatus.Pending);
        return new Friendship((int)(long)command.ExecuteScalar()!, profile, friend, FriendStatus.Pending);
    }

    public void SetStatus(int id, FriendStatus status) =>
        Execute("UPDATE friends SET status = $status WHERE id = $id", ("$id", id), ("$status", (int)status));

    public void Remove(int id) => Execute("DELETE FROM friends WHERE id = $id", ("$id", id));

    List<Friendship> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        using SqliteDataReader reader = command.ExecuteReader();

        var friends = new List<Friendship>();
        while (reader.Read())
            friends.Add(new Friendship(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), (FriendStatus)reader.GetInt32(3)));
        return friends;
    }

    void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
