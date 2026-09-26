using Microsoft.Data.Sqlite;

namespace OpenKogama.Storage;

public sealed class Database
{
    public const int FirstItemId = 900_000_000;

    static readonly string[] Migrations =
    [
        $"""
        CREATE TABLE profiles (
            id INTEGER PRIMARY KEY,
            name TEXT,
            xp INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner INTEGER NOT NULL,
            name TEXT NOT NULL,
            description TEXT NOT NULL DEFAULT '',
            category INTEGER NOT NULL,
            author INTEGER NOT NULL,
            data BLOB NOT NULL
        );

        CREATE TABLE slots (
            profile INTEGER NOT NULL,
            item INTEGER NOT NULL,
            slot INTEGER NOT NULL,
            PRIMARY KEY (profile, item)
        );

        INSERT INTO sqlite_sequence (name, seq) VALUES ('items', {FirstItemId - 1});
        """,
        """
        CREATE TABLE worlds (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL DEFAULT '',
            owner INTEGER NOT NULL DEFAULT 0,
            saved_at TEXT NOT NULL,
            data BLOB NOT NULL
        );
        """,
        """
        CREATE TABLE avatars (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner INTEGER NOT NULL,
            name TEXT NOT NULL DEFAULT '',
            active INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE avatar_parts (
            avatar INTEGER NOT NULL,
            bone TEXT NOT NULL,
            scale REAL NOT NULL,
            cubes BLOB NOT NULL,
            PRIMARY KEY (avatar, bone)
        );
        """,
        """
        CREATE TABLE avatar_accessories (
            avatar INTEGER NOT NULL,
            item INTEGER NOT NULL,
            slot INTEGER NOT NULL,
            offset REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (avatar, item)
        );
        """,
        """
        CREATE TABLE friends (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            profile INTEGER NOT NULL,
            friend INTEGER NOT NULL,
            status INTEGER NOT NULL
        );

        CREATE INDEX friends_profile ON friends (profile);
        CREATE INDEX friends_friend ON friends (friend);
        """,
        """
        CREATE TABLE images (
            type INTEGER NOT NULL,
            id INTEGER NOT NULL,
            data BLOB NOT NULL,
            PRIMARY KEY (type, id)
        );
        """,
        """
        ALTER TABLE worlds ADD COLUMN published_data BLOB;
        ALTER TABLE worlds ADD COLUMN published_at TEXT;
        """,
        """
        ALTER TABLE avatars ADD COLUMN source INTEGER;
        """,
        """
        CREATE TABLE market (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind INTEGER NOT NULL,
            owner INTEGER NOT NULL,
            source INTEGER NOT NULL,
            name TEXT NOT NULL,
            description TEXT NOT NULL DEFAULT '',
            category INTEGER NOT NULL DEFAULT 0,
            price INTEGER NOT NULL DEFAULT 0,
            data BLOB NOT NULL,
            created_at TEXT NOT NULL,
            UNIQUE (kind, source)
        );

        INSERT INTO sqlite_sequence (name, seq) VALUES ('market', 1000000);
        """,
        """
        ALTER TABLE worlds ADD COLUMN played_at TEXT;
        """,
    ];

    readonly string _connectionString;

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using SqliteConnection connection = Open();
        Execute(connection, "PRAGMA journal_mode = WAL;");

        long version = Scalar(connection, "PRAGMA user_version;");
        for (int i = (int)version; i < Migrations.Length; i++)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            Execute(connection, Migrations[i], transaction);
            Execute(connection, $"PRAGMA user_version = {i + 1};", transaction);
            transaction.Commit();
        }
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
