using Microsoft.Data.Sqlite;

namespace OpenKogama.Storage;

public sealed class SqliteWorldStore(Database database) : IWorldStore
{
    public List<WorldInfo> List()
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, owner, saved_at FROM worlds ORDER BY id";
        using SqliteDataReader reader = command.ExecuteReader();

        var worlds = new List<WorldInfo>();
        while (reader.Read())
            worlds.Add(new WorldInfo(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        return worlds;
    }

    public int Create(string name, int owner, byte[] data)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO worlds (name, owner, saved_at, data) VALUES ($name, $owner, $savedAt, $data)
            RETURNING id
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$savedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$data", data);
        return (int)(long)command.ExecuteScalar()!;
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
                name = excluded.name, owner = excluded.owner, saved_at = excluded.saved_at, data = excluded.data
            """;
        command.Parameters.AddWithValue("$id", world.Id);
        command.Parameters.AddWithValue("$name", world.Name);
        command.Parameters.AddWithValue("$owner", world.Owner);
        command.Parameters.AddWithValue("$savedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$data", world.Data);
        command.ExecuteNonQuery();
    }
}
