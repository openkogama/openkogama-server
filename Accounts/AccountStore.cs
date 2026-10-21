using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace OpenKogama.Accounts;

public sealed record Account(long Id, string Name, string? Email, string? Password, bool Verified, string? Google);

public sealed class AccountStore
{
    const int TokenSize = 32;
    const int MaxAttempts = 5;
    static readonly TimeSpan SessionLife = TimeSpan.FromDays(180);
    const string Columns = "id, name, email, password, verified, google";

    readonly string _connection;

    public AccountStore(string path)
    {
        _connection = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                email TEXT UNIQUE COLLATE NOCASE,
                password TEXT,
                verified INTEGER NOT NULL DEFAULT 0,
                google TEXT UNIQUE,
                created INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                token TEXT PRIMARY KEY,
                account INTEGER NOT NULL,
                used INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS codes (
                email TEXT NOT NULL COLLATE NOCASE,
                purpose TEXT NOT NULL,
                code TEXT NOT NULL,
                expires INTEGER NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (email, purpose)
            );
            """);
        if (EmailRequired()) Execute("""
            BEGIN;
            CREATE TABLE accounts_new (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                email TEXT UNIQUE COLLATE NOCASE,
                password TEXT,
                verified INTEGER NOT NULL DEFAULT 0,
                google TEXT UNIQUE,
                created INTEGER NOT NULL
            );
            INSERT INTO accounts_new (id, name, email, password, verified, google, created)
                SELECT id, name, email, password, verified, google, created FROM accounts;
            DROP TABLE accounts;
            ALTER TABLE accounts_new RENAME TO accounts;
            COMMIT;
            """);
    }

    bool EmailRequired()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT \"notnull\" FROM pragma_table_info('accounts') WHERE name = 'email'");
        return command.ExecuteScalar() is long required && required == 1;
    }

    public Account? Find(string login) => One($"SELECT {Columns} FROM accounts WHERE name = $v OR email = $v", login);
    public Account? ByName(string name) => One($"SELECT {Columns} FROM accounts WHERE name = $v", name);
    public Account? ByEmail(string email) => One($"SELECT {Columns} FROM accounts WHERE email = $v", email);
    public Account? ByGoogle(string subject) => One($"SELECT {Columns} FROM accounts WHERE google = $v", subject);

    public long Add(string name, string? email, string? password, bool verified, string? google)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, """
            INSERT INTO accounts (name, email, password, verified, google, created)
            VALUES ($name, $email, $password, $verified, $google, $created) RETURNING id
            """, ("$name", name), ("$email", (object?)email ?? DBNull.Value), ("$password", (object?)password ?? DBNull.Value),
            ("$verified", verified ? 1 : 0), ("$google", (object?)google ?? DBNull.Value), ("$created", Now()));
        return (long)command.ExecuteScalar()!;
    }

    public void MarkVerified(long id) => Execute("UPDATE accounts SET verified = 1 WHERE id = $id", ("$id", id));
    public void SetEmail(long id, string email) => Execute("UPDATE accounts SET email = $email, verified = 1 WHERE id = $id", ("$id", id), ("$email", email));
    public void SetPassword(long id, string hash) => Execute("UPDATE accounts SET password = $hash, verified = 1 WHERE id = $id", ("$id", id), ("$hash", hash));
    public void LinkGoogle(long id, string subject) => Execute("UPDATE accounts SET google = $google, verified = 1 WHERE id = $id", ("$id", id), ("$google", subject));
    public void DropUnverified(TimeSpan age) => Execute("DELETE FROM accounts WHERE verified = 0 AND created < $before", ("$before", Now() - (long)age.TotalSeconds));

    public string StartSession(long account)
    {
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSize));
        Execute("INSERT INTO sessions (token, account, used) VALUES ($token, $account, $used)", ("$token", Digest(token)), ("$account", account), ("$used", Now()));
        return token;
    }

    public Account? Session(string token)
    {
        Execute("DELETE FROM sessions WHERE used < $before", ("$before", Now() - (long)SessionLife.TotalSeconds));
        Account? account = One($"SELECT {Prefixed("a")} FROM sessions s JOIN accounts a ON a.id = s.account WHERE s.token = $v", Digest(token));
        if (account is not null) Execute("UPDATE sessions SET used = $used WHERE token = $token", ("$token", Digest(token)), ("$used", Now()));
        return account;
    }

    public void EndSession(string token) => Execute("DELETE FROM sessions WHERE token = $token", ("$token", Digest(token)));
    public void EndSessions(long account) => Execute("DELETE FROM sessions WHERE account = $account", ("$account", account));

    public void SetCode(string email, string purpose, string code, TimeSpan life) => Execute("""
        INSERT INTO codes (email, purpose, code, expires) VALUES ($email, $purpose, $code, $expires)
        ON CONFLICT (email, purpose) DO UPDATE SET code = excluded.code, expires = excluded.expires, attempts = 0
        """, ("$email", email), ("$purpose", purpose), ("$code", Digest(code)), ("$expires", Now() + (long)life.TotalSeconds));

    public bool UseCode(string email, string purpose, string code)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand find = Command(connection, "SELECT code, expires, attempts FROM codes WHERE email = $email AND purpose = $purpose",
            ("$email", email), ("$purpose", purpose));
        using SqliteDataReader reader = find.ExecuteReader();
        if (!reader.Read()) return false;
        bool valid = reader.GetInt64(1) >= Now() && reader.GetInt32(2) < MaxAttempts
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(reader.GetString(0)), Encoding.ASCII.GetBytes(Digest(code)));
        reader.Close();
        Execute(valid ? "DELETE FROM codes WHERE email = $email AND purpose = $purpose" : "UPDATE codes SET attempts = attempts + 1 WHERE email = $email AND purpose = $purpose",
            ("$email", email), ("$purpose", purpose));
        return valid;
    }

    static string Prefixed(string table) => string.Join(", ", Columns.Split(", ").Select(column => table + "." + column));

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    Account? One(string sql, string value)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, sql, ("$v", value));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new Account(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) != 0, reader.IsDBNull(5) ? null : reader.GetString(5))
            : null;
    }

    void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connection);
        connection.Open();
        return connection;
    }

    static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
