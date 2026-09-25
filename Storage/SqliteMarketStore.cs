using Microsoft.Data.Sqlite;

namespace OpenKogama.Storage;

public sealed class SqliteMarketStore(Database database) : IMarketStore
{
    const string Columns = "id, kind, owner, source, name, description, category, price, data";

    public List<Listing> List(ListingKind kind) =>
        Query($"SELECT {Columns} FROM market WHERE kind = $kind ORDER BY id", ("$kind", (int)kind));

    public Listing? Find(int id) =>
        Query($"SELECT {Columns} FROM market WHERE id = $id", ("$id", id)).FirstOrDefault();

    public Listing? FindBySource(ListingKind kind, int source) =>
        Query($"SELECT {Columns} FROM market WHERE kind = $kind AND source = $source", ("$kind", (int)kind), ("$source", source)).FirstOrDefault();

    public int Put(ListingKind kind, int owner, int source, string name, string description, int category, int price, byte[] data)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO market (kind, owner, source, name, description, category, price, data, created_at)
            VALUES ($kind, $owner, $source, $name, $description, $category, $price, $data, $createdAt)
            ON CONFLICT (kind, source) DO UPDATE SET
                name = excluded.name, description = excluded.description, category = excluded.category,
                price = excluded.price, data = excluded.data
            RETURNING id
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$price", price);
        command.Parameters.AddWithValue("$data", data);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        return (int)(long)command.ExecuteScalar()!;
    }

    public bool Remove(ListingKind kind, int owner, int source)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM market WHERE kind = $kind AND owner = $owner AND source = $source";
        command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteNonQuery() > 0;
    }

    List<Listing> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        using SqliteDataReader reader = command.ExecuteReader();

        var listings = new List<Listing>();
        while (reader.Read())
        {
            listings.Add(new Listing(reader.GetInt32(0), (ListingKind)reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), (byte[])reader[8]));
        }
        return listings;
    }
}
