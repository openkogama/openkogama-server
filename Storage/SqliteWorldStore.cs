using Microsoft.Data.Sqlite;

namespace OpenKogama.Storage;

public sealed class SqliteWorldStore(Database database) : IWorldStore
{
    public List<WorldInfo> List()
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, owner, saved_at, published_at, played_at FROM worlds ORDER BY played_at IS NULL, played_at DESC, id";
        using SqliteDataReader reader = command.ExecuteReader();

        var worlds = new List<WorldInfo>();
        while (reader.Read())
            worlds.Add(new WorldInfo(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return worlds;
    }

    public int Create(string name, int owner, byte[] data)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO worlds (name, owner, saved_at, played_at, data) VALUES ($name, $owner, $savedAt, $savedAt, $data)
            RETURNING id
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$savedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$data", data);
        int id = (int)(long)command.ExecuteScalar()!;
        Revision.Bump();
        return id;
    }

    public StoredWorld? World(int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name, owner, data FROM worlds WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read()) return null;
        return new StoredWorld(id, reader.GetString(0), reader.GetInt32(1), (byte[])reader[2]);
    }

    public void SaveWorld(StoredWorld world)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO worlds (id, name, owner, saved_at, data)
            VALUES ($id, $name, $owner, $savedAt, $data)
            ON CONFLICT (id) DO UPDATE SET
                saved_at = excluded.saved_at, data = excluded.data
            """;
        command.Parameters.AddWithValue("$id", world.Id);
        command.Parameters.AddWithValue("$name", world.Name);
        command.Parameters.AddWithValue("$owner", world.Owner);
        command.Parameters.AddWithValue("$savedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$data", world.Data);
        command.ExecuteNonQuery();
    }

    public byte[]? Published(int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT published_data FROM worlds WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as byte[];
    }

    public void Publish(int id, byte[] data)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE worlds SET published_data = $data, published_at = $publishedAt WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$data", data);
        command.Parameters.AddWithValue("$publishedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        Revision.Bump();
    }

    public void MarkPlayed(int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE worlds SET played_at = $playedAt WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$playedAt", DateTime.UtcNow.ToString("O"));
        Changed(command.ExecuteNonQuery() > 0);
    }

    public bool Rename(int id, string name)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE worlds SET name = $name WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        return Changed(command.ExecuteNonQuery() > 0);
    }

    public bool Delete(int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM worlds WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Changed(command.ExecuteNonQuery() > 0);
    }

    static bool Changed(bool changed)
    {
        if (changed) Revision.Bump();
        return changed;
    }
}
