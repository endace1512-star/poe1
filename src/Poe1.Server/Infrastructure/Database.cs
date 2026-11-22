using Dapper;
using Microsoft.Data.Sqlite;

namespace Poe1.Infrastructure;

/// <summary>SQLite storage for snapshots and farm sessions.</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(AppPaths paths) : this(paths.DatabaseFile) { }

    public Database(string file)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Migrate();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode = WAL;");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS snapshots (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                ts            TEXT    NOT NULL,
                league        TEXT    NOT NULL,
                total_chaos   REAL    NOT NULL,
                divine_chaos  REAL    NOT NULL,
                item_count    INTEGER NOT NULL,
                items_pruned  INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_snapshots_league_ts ON snapshots(league, ts);

            CREATE TABLE IF NOT EXISTS snapshot_sources (
                snapshot_id  INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
                source_key   TEXT    NOT NULL,
                name         TEXT    NOT NULL,
                kind         TEXT    NOT NULL,
                idx          INTEGER NOT NULL,
                colour       TEXT,
                item_count   INTEGER NOT NULL,
                value_chaos  REAL    NOT NULL,
                PRIMARY KEY (snapshot_id, source_key)
            );

            CREATE TABLE IF NOT EXISTS snapshot_items (
                snapshot_id  INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
                source_key   TEXT    NOT NULL,
                item_key     TEXT    NOT NULL,
                name         TEXT    NOT NULL,
                category     TEXT    NOT NULL,
                icon         TEXT,
                frame_type   INTEGER NOT NULL,
                quantity     REAL    NOT NULL,
                unit_chaos   REAL    NOT NULL,
                total_chaos  REAL    NOT NULL,
                PRIMARY KEY (snapshot_id, source_key, item_key)
            );
            CREATE INDEX IF NOT EXISTS ix_snapshot_items_key ON snapshot_items(item_key);

            CREATE TABLE IF NOT EXISTS farm_sessions (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                name               TEXT    NOT NULL,
                league             TEXT    NOT NULL,
                start_ts           TEXT    NOT NULL,
                end_ts             TEXT,
                start_snapshot_id  INTEGER NOT NULL REFERENCES snapshots(id),
                end_snapshot_id    INTEGER REFERENCES snapshots(id),
                notes              TEXT
            );

            CREATE TABLE IF NOT EXISTS builds (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL,
                source      TEXT,
                xml         TEXT    NOT NULL,
                character   TEXT,
                created_ts  TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS build_ignores (
                build_id  INTEGER NOT NULL REFERENCES builds(id) ON DELETE CASCADE,
                key       TEXT    NOT NULL,
                reason    TEXT    NOT NULL,
                PRIMARY KEY (build_id, key)
            );
            """);
    }
}
