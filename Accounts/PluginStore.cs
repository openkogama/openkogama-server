using Microsoft.Data.Sqlite;

namespace OpenKogama.Accounts;

public enum PluginState { Pending, Public, Hidden }

public sealed record Plugin(long Id, string Slug, long Owner, string Author, string Name, string Summary, string Description,
    string? Video, string? Icon, string[] Images, PluginState State, long Created, long Updated, long Downloads, long Ratings, long Stars);

public sealed record PluginVersion(long Id, long Plugin, string Version, string Api, string File, string Name, long Size, string Changes, long Created, long Downloads, ScanResult? Scan, bool Pending);

public sealed record PluginReport(string Slug, string Name, string Reporter, string Reason, long Created);

public sealed class PluginStore
{
    const string Columns = "p.id, p.slug, p.owner, a.name, p.name, p.summary, p.description, p.video, p.icon, p.images, p.state, p.created, p.updated, p.downloads, "
        + "(SELECT COUNT(*) FROM plugin_ratings r WHERE r.plugin = p.id) AS ratings, (SELECT COALESCE(SUM(r.stars), 0) FROM plugin_ratings r WHERE r.plugin = p.id) AS stars";
    const int MinRatings = 3;
    const string Joined = "plugins p JOIN accounts a ON a.id = p.owner";
    const string VersionColumns = "id, plugin, version, api, file, name, size, changes, created, downloads, scan_malicious, scan_suspicious, scan_total, state";
    const string HasVersion = "EXISTS (SELECT 1 FROM plugin_versions v WHERE v.plugin = p.id AND v.state = 1)";
    const string HasPendingVersion = "EXISTS (SELECT 1 FROM plugin_versions v WHERE v.plugin = p.id AND v.state = 0)";
    const string HasAnyVersion = "EXISTS (SELECT 1 FROM plugin_versions v WHERE v.plugin = p.id)";

    readonly string _connection;

    public PluginStore(string path)
    {
        _connection = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Execute("""
            CREATE TABLE IF NOT EXISTS plugins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                owner INTEGER NOT NULL,
                name TEXT NOT NULL,
                summary TEXT NOT NULL,
                description TEXT NOT NULL,
                video TEXT,
                icon TEXT,
                images TEXT NOT NULL DEFAULT '',
                state INTEGER NOT NULL DEFAULT 0,
                created INTEGER NOT NULL,
                updated INTEGER NOT NULL,
                downloads INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS plugins_owner ON plugins (owner);
            CREATE TABLE IF NOT EXISTS plugin_versions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin INTEGER NOT NULL,
                version TEXT NOT NULL,
                api TEXT NOT NULL,
                file TEXT NOT NULL,
                name TEXT NOT NULL,
                size INTEGER NOT NULL,
                changes TEXT NOT NULL,
                created INTEGER NOT NULL,
                downloads INTEGER NOT NULL DEFAULT 0,
                UNIQUE (plugin, version)
            );
            CREATE TABLE IF NOT EXISTS plugin_reports (
                plugin INTEGER NOT NULL,
                account INTEGER NOT NULL,
                reason TEXT NOT NULL,
                created INTEGER NOT NULL,
                PRIMARY KEY (plugin, account)
            );
            CREATE TABLE IF NOT EXISTS plugin_ratings (
                plugin INTEGER NOT NULL,
                account INTEGER NOT NULL,
                stars INTEGER NOT NULL,
                created INTEGER NOT NULL,
                PRIMARY KEY (plugin, account)
            );
            """);
        if (!HasColumn("plugin_versions", "scan_total"))
            Execute("""
                ALTER TABLE plugin_versions ADD COLUMN scan_malicious INTEGER;
                ALTER TABLE plugin_versions ADD COLUMN scan_suspicious INTEGER;
                ALTER TABLE plugin_versions ADD COLUMN scan_total INTEGER;
                """);
        if (!HasColumn("plugin_versions", "state")) Execute("ALTER TABLE plugin_versions ADD COLUMN state INTEGER NOT NULL DEFAULT 1");
    }

    bool HasColumn(string table, string name)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $name", ("$table", table), ("$name", name));
        return command.ExecuteScalar() is long count && count > 0;
    }

    public IReadOnlyList<Plugin> Public(string? search, string sort, int skip, int take)
    {
        string order = sort switch
        {
            "new" => "p.created DESC",
            "updated" => "p.updated DESC",
            "rated" => $"CASE WHEN ratings >= {MinRatings} THEN stars * 1.0 / ratings ELSE 0 END DESC, ratings DESC, p.downloads DESC",
            _ => "p.downloads DESC, p.updated DESC",
        };
        string filter = string.IsNullOrEmpty(search) ? "" : "AND (p.name LIKE $q ESCAPE '\\' OR p.summary LIKE $q ESCAPE '\\' OR a.name LIKE $q ESCAPE '\\')";
        return Many($"SELECT {Columns} FROM {Joined} WHERE p.state = 1 AND {HasVersion} {filter} ORDER BY {order} LIMIT $take OFFSET $skip",
            ("$q", Like(search ?? "")), ("$take", take), ("$skip", skip));
    }

    public Plugin? BySlug(string slug) => Many($"SELECT {Columns} FROM {Joined} WHERE p.slug = $slug", ("$slug", slug)).FirstOrDefault();

    public IReadOnlyList<Plugin> ByOwner(long owner) => Many($"SELECT {Columns} FROM {Joined} WHERE p.owner = $owner ORDER BY p.updated DESC", ("$owner", owner));

    public IReadOnlyList<Plugin> Waiting() => Many($"SELECT {Columns} FROM {Joined} WHERE (p.state = 0 AND {HasAnyVersion}) OR (p.state = 1 AND {HasPendingVersion}) ORDER BY p.updated");

    public bool Taken(string slug) => Scalar("SELECT COUNT(*) FROM plugins WHERE slug = $slug", ("$slug", slug)) > 0;

    public long Owned(long owner) => Scalar("SELECT COUNT(*) FROM plugins WHERE owner = $owner", ("$owner", owner));

    public long Add(long owner, string slug, string name, string summary, string description, string? video, string? icon, string[] images) => Scalar("""
        INSERT INTO plugins (slug, owner, name, summary, description, video, icon, images, created, updated)
        VALUES ($slug, $owner, $name, $summary, $description, $video, $icon, $images, $now, $now) RETURNING id
        """, ("$slug", slug), ("$owner", owner), ("$name", name), ("$summary", summary), ("$description", description),
        ("$video", Value(video)), ("$icon", Value(icon)), ("$images", string.Join(' ', images)), ("$now", Now()));

    public void Update(long id, string name, string summary, string description, string? video, string? icon, string[] images) => Execute("""
        UPDATE plugins SET name = $name, summary = $summary, description = $description, video = $video, icon = $icon, images = $images, updated = $now
        WHERE id = $id
        """, ("$id", id), ("$name", name), ("$summary", summary), ("$description", description),
        ("$video", Value(video)), ("$icon", Value(icon)), ("$images", string.Join(' ', images)), ("$now", Now()));

    public void SetState(long id, PluginState state) => Execute("UPDATE plugins SET state = $state WHERE id = $id", ("$id", id), ("$state", (int)state));

    public void Delete(long id) => Execute("""
        BEGIN;
        DELETE FROM plugin_versions WHERE plugin = $id;
        DELETE FROM plugin_reports WHERE plugin = $id;
        DELETE FROM plugin_ratings WHERE plugin = $id;
        DELETE FROM plugins WHERE id = $id;
        COMMIT;
        """, ("$id", id));

    public void DeleteOwner(long owner) => Execute("""
        BEGIN;
        DELETE FROM plugin_versions WHERE plugin IN (SELECT id FROM plugins WHERE owner = $owner);
        DELETE FROM plugin_reports WHERE account = $owner OR plugin IN (SELECT id FROM plugins WHERE owner = $owner);
        DELETE FROM plugin_ratings WHERE account = $owner OR plugin IN (SELECT id FROM plugins WHERE owner = $owner);
        DELETE FROM plugins WHERE owner = $owner;
        COMMIT;
        """, ("$owner", owner));

    public IReadOnlyList<PluginVersion> Versions(long plugin, bool all = false) =>
        Versions("WHERE plugin = $plugin AND (state = 1 OR $all = 1) ORDER BY id DESC", ("$plugin", plugin), ("$all", all ? 1 : 0));

    public PluginVersion? Version(long plugin, string? version, bool all = false) => version is null
        ? Versions("WHERE plugin = $plugin AND (state = 1 OR $all = 1) ORDER BY id DESC LIMIT 1", ("$plugin", plugin), ("$all", all ? 1 : 0)).FirstOrDefault()
        : Versions("WHERE plugin = $plugin AND version = $version AND (state = 1 OR $all = 1)", ("$plugin", plugin), ("$version", version), ("$all", all ? 1 : 0)).FirstOrDefault();

    public void ApproveVersion(long id) => Execute("UPDATE plugin_versions SET state = 1 WHERE id = $id", ("$id", id));

    public void ApproveVersions(long plugin) => Execute("UPDATE plugin_versions SET state = 1 WHERE plugin = $plugin", ("$plugin", plugin));

    public PluginVersion? AddVersion(long plugin, string version, string api, string file, string name, long size, string changes)
    {
        Execute("""
            BEGIN;
            INSERT INTO plugin_versions (plugin, version, api, file, name, size, changes, created, state)
            VALUES ($plugin, $version, $api, $file, $name, $size, $changes, $now, 0);
            UPDATE plugins SET updated = $now WHERE id = $plugin;
            COMMIT;
            """, ("$plugin", plugin), ("$version", version), ("$api", api), ("$file", file), ("$name", name), ("$size", size), ("$changes", changes), ("$now", Now()));
        return Version(plugin, version, all: true);
    }

    public void SetScan(long id, ScanResult scan) => Execute(
        "UPDATE plugin_versions SET scan_malicious = $malicious, scan_suspicious = $suspicious, scan_total = $total WHERE id = $id",
        ("$id", id), ("$malicious", scan.Malicious), ("$suspicious", scan.Suspicious), ("$total", scan.Total));

    public IReadOnlyList<PluginVersion> Unscanned() => Versions("WHERE scan_total IS NULL ORDER BY id");

    public void DeleteVersion(long id) => Execute("DELETE FROM plugin_versions WHERE id = $id", ("$id", id));

    public void CountDownload(PluginVersion version) => Execute("""
        UPDATE plugin_versions SET downloads = downloads + 1 WHERE id = $id;
        UPDATE plugins SET downloads = downloads + 1 WHERE id = $plugin;
        """, ("$id", version.Id), ("$plugin", version.Plugin));

    public void Report(long plugin, long account, string reason) => Execute("""
        INSERT INTO plugin_reports (plugin, account, reason, created) VALUES ($plugin, $account, $reason, $now)
        ON CONFLICT (plugin, account) DO UPDATE SET reason = excluded.reason, created = excluded.created
        """, ("$plugin", plugin), ("$account", account), ("$reason", reason), ("$now", Now()));

    public IReadOnlyList<PluginReport> Reports()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, """
            SELECT p.slug, p.name, a.name, r.reason, r.created FROM plugin_reports r
            JOIN plugins p ON p.id = r.plugin JOIN accounts a ON a.id = r.account
            ORDER BY r.created DESC
            """);
        using SqliteDataReader reader = command.ExecuteReader();
        var reports = new List<PluginReport>();
        while (reader.Read()) reports.Add(new PluginReport(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
        return reports;
    }

    public void ClearReports(long plugin) => Execute("DELETE FROM plugin_reports WHERE plugin = $plugin", ("$plugin", plugin));

    public void Rate(long plugin, long account, int stars) => Execute("""
        INSERT INTO plugin_ratings (plugin, account, stars, created) VALUES ($plugin, $account, $stars, $now)
        ON CONFLICT (plugin, account) DO UPDATE SET stars = excluded.stars, created = excluded.created
        """, ("$plugin", plugin), ("$account", account), ("$stars", stars), ("$now", Now()));

    public int MyRating(long plugin, long account) =>
        (int)Scalar("SELECT COALESCE((SELECT stars FROM plugin_ratings WHERE plugin = $plugin AND account = $account), 0)", ("$plugin", plugin), ("$account", account));

    public static double? Average(Plugin plugin) => plugin.Ratings >= MinRatings ? Math.Round(plugin.Stars / (double)plugin.Ratings, 1) : null;

    public HashSet<string> Files()
    {
        var files = new HashSet<string>();
        using SqliteConnection connection = Open();
        using (SqliteCommand command = Command(connection, "SELECT icon, images FROM plugins"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                if (!reader.IsDBNull(0)) files.Add(reader.GetString(0));
                files.UnionWith(reader.GetString(1).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        using (SqliteCommand command = Command(connection, "SELECT file FROM plugin_versions"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read()) files.Add(reader.GetString(0));
        return files;
    }

    static string Like(string text) => "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    static object Value(string? text) => (object?)text ?? DBNull.Value;

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    List<Plugin> Many(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, sql, parameters);
        using SqliteDataReader reader = command.ExecuteReader();
        var plugins = new List<Plugin>();
        while (reader.Read())
            plugins.Add(new Plugin(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9).Split(' ', StringSplitOptions.RemoveEmptyEntries), (PluginState)reader.GetInt32(10),
                reader.GetInt64(11), reader.GetInt64(12), reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15)));
        return plugins;
    }

    List<PluginVersion> Versions(string where, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, $"SELECT {VersionColumns} FROM plugin_versions {where}", parameters);
        using SqliteDataReader reader = command.ExecuteReader();
        var versions = new List<PluginVersion>();
        while (reader.Read())
            versions.Add(new PluginVersion(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetInt64(8), reader.GetInt64(9),
                reader.IsDBNull(12) ? null : new ScanResult(reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12)), reader.GetInt32(13) == 0));
        return versions;
    }

    long Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, sql, parameters);
        return (long)command.ExecuteScalar()!;
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
