using Microsoft.Data.Sqlite;

namespace OpenKogama.Storage;

public sealed class SqliteImageStore(Database database) : IImageStore
{
    public byte[]? Image(int type, int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM images WHERE type = $type AND id = $id";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as byte[];
    }

    public void SaveImage(int type, int id, byte[] data)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO images (type, id, data) VALUES ($type, $id, $data)
            ON CONFLICT (type, id) DO UPDATE SET data = excluded.data
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$data", data);
        command.ExecuteNonQuery();
        Revision.Bump();
    }

    public void DeleteImage(int type, int id)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM images WHERE type = $type AND id = $id";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        Revision.Bump();
    }
}
