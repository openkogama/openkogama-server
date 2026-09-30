using Microsoft.Data.Sqlite;
using OpenKogama.Game;

namespace OpenKogama.Storage;

public sealed class SqliteProfileStore(Database database) : IProfileStore
{
    public int Xp(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT xp FROM profiles WHERE id = $profile", ("$profile", profile));
        return command.ExecuteScalar() is long xp ? (int)xp : 0;
    }

    public int AddXp(int profile, int amount)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, xp) VALUES ($profile, $amount)
            ON CONFLICT (id) DO UPDATE SET xp = xp + $amount
            RETURNING xp
            """, ("$profile", profile), ("$amount", amount));
        return (int)(long)command.ExecuteScalar()!;
    }

    public int Gold(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT gold FROM profiles WHERE id = $profile", ("$profile", profile));
        return command.ExecuteScalar() is long gold ? (int)gold : 0;
    }

    public int AddGold(int profile, int amount)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, gold) VALUES ($profile, MAX(0, $amount))
            ON CONFLICT (id) DO UPDATE SET gold = MAX(0, gold + $amount)
            RETURNING gold
            """, ("$profile", profile), ("$amount", amount));
        return (int)(long)command.ExecuteScalar()!;
    }

    public (int Level, Dictionary<int, int> Unseen) LevelRewards(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT gold_level, unseen_gold FROM profiles WHERE id = $profile", ("$profile", profile));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return (1, []);
        Dictionary<int, int> unseen = reader.IsDBNull(1) ? [] : System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, int>>(reader.GetString(1)) ?? [];
        return (reader.GetInt32(0), unseen);
    }

    public void SetLevelRewards(int profile, int level, Dictionary<int, int> unseen)
    {
        using SqliteConnection connection = database.Open();
        object stored = unseen.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(unseen) : DBNull.Value;
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, gold_level, unseen_gold) VALUES ($profile, $level, $unseen)
            ON CONFLICT (id) DO UPDATE SET gold_level = $level, unseen_gold = $unseen
            """, ("$profile", profile), ("$level", level), ("$unseen", stored));
        command.ExecuteNonQuery();
    }

    public (int Tier, int Seen) GameTier(int profile, int world)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT tier, seen FROM game_tiers WHERE profile = $profile AND world = $world",
            ("$profile", profile), ("$world", world));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    public void SetGameTier(int profile, int world, int tier, int seen)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO game_tiers (profile, world, tier, seen) VALUES ($profile, $world, $tier, $seen)
            ON CONFLICT (profile, world) DO UPDATE SET tier = $tier, seen = $seen
            """, ("$profile", profile), ("$world", world), ("$tier", tier), ("$seen", seen));
        command.ExecuteNonQuery();
    }

    public int CoinBoost(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT coin_boost FROM profiles WHERE id = $profile", ("$profile", profile));
        return command.ExecuteScalar() is long left ? (int)left : 0;
    }

    public int AddCoinBoost(int profile, int milliseconds)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, coin_boost) VALUES ($profile, MAX(0, $milliseconds))
            ON CONFLICT (id) DO UPDATE SET coin_boost = MAX(0, coin_boost + $milliseconds)
            RETURNING coin_boost
            """, ("$profile", profile), ("$milliseconds", milliseconds));
        return (int)(long)command.ExecuteScalar()!;
    }

    public (int Spins, long Next) Spins(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT spins, next_spin FROM profiles WHERE id = $profile", ("$profile", profile));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetInt64(1)) : (0, 0);
    }

    public void SetSpins(int profile, int spins, long next)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, spins, next_spin) VALUES ($profile, $spins, $next)
            ON CONFLICT (id) DO UPDATE SET spins = $spins, next_spin = $next
            """, ("$profile", profile), ("$spins", spins), ("$next", next));
        command.ExecuteNonQuery();
    }

    public byte[]? FirstTime(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT first_time FROM profiles WHERE id = $profile", ("$profile", profile));
        return command.ExecuteScalar() as byte[];
    }

    public void SetFirstTime(int profile, byte[] state)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO profiles (id, first_time) VALUES ($profile, $state)
            ON CONFLICT (id) DO UPDATE SET first_time = $state
            """, ("$profile", profile), ("$state", state));
        command.ExecuteNonQuery();
    }

    public List<Item> Items(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection,
            "SELECT id, name, description, category, author, data FROM items WHERE owner = $profile ORDER BY id",
            ("$profile", profile));
        using SqliteDataReader reader = command.ExecuteReader();

        var items = new List<Item>();
        while (reader.Read())
        {
            items.Add(new Item
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Category = reader.GetInt32(3),
                Author = reader.GetInt32(4),
                Data = Convert.ToBase64String((byte[])reader[5]),
            });
        }
        return items;
    }

    public Item AddItem(int profile, string name, int category, byte[] data, int author)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO items (owner, name, category, author, data) VALUES ($profile, $name, $category, $author, $data)
            RETURNING id
            """, ("$profile", profile), ("$name", name), ("$category", category), ("$author", author), ("$data", data));

        return new Item
        {
            Id = (int)(long)command.ExecuteScalar()!,
            Name = name,
            Category = category,
            Author = author,
            Data = Convert.ToBase64String(data),
        };
    }

    public bool RemoveItem(int profile, int itemId)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand remove = Command(connection, "DELETE FROM items WHERE id = $item AND owner = $profile", ("$item", itemId), ("$profile", profile));
        remove.Transaction = transaction;
        bool removed = remove.ExecuteNonQuery() > 0;

        using SqliteCommand slot = Command(connection, "DELETE FROM slots WHERE profile = $profile AND item = $item", ("$item", itemId), ("$profile", profile));
        slot.Transaction = transaction;
        slot.ExecuteNonQuery();

        transaction.Commit();
        return removed;
    }

    public Dictionary<int, int> Slots(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT item, slot FROM slots WHERE profile = $profile", ("$profile", profile));
        using SqliteDataReader reader = command.ExecuteReader();

        var slots = new Dictionary<int, int>();
        while (reader.Read())
            slots[reader.GetInt32(0)] = reader.GetInt32(1);
        return slots;
    }

    public void SetSlots(int profile, Dictionary<int, int> slots)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand clear = Command(connection, "DELETE FROM slots WHERE profile = $profile", ("$profile", profile));
        clear.Transaction = transaction;
        clear.ExecuteNonQuery();

        foreach ((int item, int slot) in slots)
        {
            using SqliteCommand insert = Command(connection, "INSERT INTO slots (profile, item, slot) VALUES ($profile, $item, $slot)",
                ("$profile", profile), ("$item", item), ("$slot", slot));
            insert.Transaction = transaction;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<StoredAvatar> Avatars(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT id, active, source FROM avatars WHERE owner = $profile ORDER BY id", ("$profile", profile));
        using SqliteDataReader reader = command.ExecuteReader();

        var avatars = new List<StoredAvatar>();
        while (reader.Read())
            avatars.Add(new StoredAvatar(reader.GetInt32(0), reader.GetInt32(1) == 1, reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        return avatars;
    }

    public int ActiveAvatar(int profile)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand find = Command(connection, "SELECT id FROM avatars WHERE owner = $profile AND active = 1", ("$profile", profile));
        find.Transaction = transaction;
        if (find.ExecuteScalar() is long id) return (int)id;

        using SqliteCommand insert = Command(connection, "INSERT INTO avatars (owner, active) VALUES ($profile, 1) RETURNING id", ("$profile", profile));
        insert.Transaction = transaction;
        int created = (int)(long)insert.ExecuteScalar()!;
        transaction.Commit();
        return created;
    }

    public int AddAvatar(int profile, int? source)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand insert = Command(connection, "INSERT INTO avatars (owner, active, source) VALUES ($profile, 0, $source) RETURNING id",
            ("$profile", profile), ("$source", (object?)source ?? DBNull.Value));
        return (int)(long)insert.ExecuteScalar()!;
    }

    public int? AvatarSource(int avatar)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT source FROM avatars WHERE id = $avatar", ("$avatar", avatar));
        return command.ExecuteScalar() is long source ? (int)source : null;
    }

    public List<AvatarPart>? AvatarParts(int avatar)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT bone, scale, cubes FROM avatar_parts WHERE avatar = $avatar", ("$avatar", avatar));
        using SqliteDataReader reader = command.ExecuteReader();

        var parts = new List<AvatarPart>();
        while (reader.Read())
        {
            parts.Add(new AvatarPart
            {
                Bone = reader.GetString(0),
                Scale = reader.GetFloat(1),
                Cubes = Convert.ToBase64String((byte[])reader[2]),
            });
        }
        return parts.Count > 0 ? parts : null;
    }

    public void SaveAvatar(int avatar, List<AvatarPart> parts)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (AvatarPart part in parts)
        {
            using SqliteCommand save = Command(connection, """
                INSERT INTO avatar_parts (avatar, bone, scale, cubes) VALUES ($avatar, $bone, $scale, $cubes)
                ON CONFLICT (avatar, bone) DO UPDATE SET scale = excluded.scale, cubes = excluded.cubes
                """, ("$avatar", avatar), ("$bone", part.Bone), ("$scale", part.Scale), ("$cubes", part.CubeData));
            save.Transaction = transaction;
            save.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SetActiveAvatar(int profile, int avatar)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection,
            "UPDATE avatars SET active = CASE WHEN id = $avatar THEN 1 ELSE 0 END WHERE owner = $profile AND EXISTS (SELECT 1 FROM avatars WHERE id = $avatar AND owner = $profile)",
            ("$profile", profile), ("$avatar", avatar));
        command.ExecuteNonQuery();
    }

    public List<WornAccessory> Accessories(int avatar)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection, "SELECT item, slot, offset, scale FROM avatar_accessories WHERE avatar = $avatar", ("$avatar", avatar));
        using SqliteDataReader reader = command.ExecuteReader();

        var accessories = new List<WornAccessory>();
        while (reader.Read())
            accessories.Add(new WornAccessory(reader.GetInt32(0), reader.GetInt32(1), reader.GetFloat(2), reader.GetFloat(3)));
        return accessories;
    }

    public void SetAccessory(int avatar, int item, int slot, float offset, float scale = 1f)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = slot == 0
            ? Command(connection, "DELETE FROM avatar_accessories WHERE avatar = $avatar AND item = $item", ("$avatar", avatar), ("$item", item))
            : Command(connection, """
                INSERT INTO avatar_accessories (avatar, item, slot, offset, scale) VALUES ($avatar, $item, $slot, $offset, $scale)
                ON CONFLICT (avatar, item) DO UPDATE SET slot = excluded.slot, offset = excluded.offset, scale = excluded.scale
                """, ("$avatar", avatar), ("$item", item), ("$slot", slot), ("$offset", offset), ("$scale", scale));
        command.ExecuteNonQuery();
    }

    public void SetAccessoryScale(int avatar, int slot, float scale)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection,
            "UPDATE avatar_accessories SET scale = $scale WHERE avatar = $avatar AND slot = $slot",
            ("$avatar", avatar), ("$slot", slot), ("$scale", scale));
        command.ExecuteNonQuery();
    }

    public void ClearAccessorySlot(int avatar, int slot)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection,
            "DELETE FROM avatar_accessories WHERE avatar = $avatar AND slot = $slot", ("$avatar", avatar), ("$slot", slot));
        command.ExecuteNonQuery();
    }

    public void SetAccessoryOffset(int avatar, int slot, float offset)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Command(connection,
            "UPDATE avatar_accessories SET offset = $offset WHERE avatar = $avatar AND slot = $slot",
            ("$avatar", avatar), ("$slot", slot), ("$offset", offset));
        command.ExecuteNonQuery();
    }

    static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}
