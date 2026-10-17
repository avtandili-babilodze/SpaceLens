using System.Globalization;
using Microsoft.Data.Sqlite;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Models;

namespace SpaceLens.Core.Repositories;

public sealed class SqliteScanHistoryRepository(AppPaths paths) : IScanHistoryRepository
{
    /// <summary>Version 1 stored every file of every scan; its tables can't be converted, so they are dropped.</summary>
    private const int SchemaVersion = 2;

    private const string DropTables = """
        DROP TABLE IF EXISTS Entries;
        DROP TABLE IF EXISTS SnapshotFiles;
        DROP TABLE IF EXISTS SnapshotFolders;
        DROP TABLE IF EXISTS Snapshots;
        """;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Snapshots (
            Id INTEGER PRIMARY KEY,
            RootPath TEXT NOT NULL COLLATE NOCASE,
            CreatedUtc TEXT NOT NULL,
            TotalBytes INTEGER NOT NULL,
            FileCount INTEGER NOT NULL,
            FolderCount INTEGER NOT NULL,
            UnreadableFolderCount INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS SnapshotFolders (
            SnapshotId INTEGER NOT NULL REFERENCES Snapshots(Id) ON DELETE CASCADE,
            Path TEXT NOT NULL COLLATE NOCASE,
            Bytes INTEGER NOT NULL,
            FileCount INTEGER NOT NULL,
            PRIMARY KEY (SnapshotId, Path)) WITHOUT ROWID;
        CREATE TABLE IF NOT EXISTS SnapshotFiles (
            SnapshotId INTEGER NOT NULL REFERENCES Snapshots(Id) ON DELETE CASCADE,
            Path TEXT NOT NULL COLLATE NOCASE,
            Bytes INTEGER NOT NULL,
            LastWriteUtc TEXT NOT NULL,
            PRIMARY KEY (SnapshotId, Path)) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS IX_Snapshots_Root ON Snapshots(RootPath, Id);
        """;

    private const string SnapshotColumns = "Id, RootPath, CreatedUtc, TotalBytes, FileCount, FolderCount, UnreadableFolderCount";

    public Task InitializeAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        using var versionQuery = connection.CreateCommand();
        versionQuery.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionQuery.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
        {
            return true;
        }

        Execute(connection, DropTables);
        Execute(connection, Schema);
        Execute(connection, $"PRAGMA user_version = {SchemaVersion};");
        Execute(connection, "VACUUM;");
        return true;
    }, cancellationToken);

    public Task<StorageSnapshot> SaveAsync(SnapshotRecord record, CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var transaction = connection.BeginTransaction();

        using var insertSnapshot = connection.CreateCommand();
        insertSnapshot.Transaction = transaction;
        insertSnapshot.CommandText = """
            INSERT INTO Snapshots (RootPath, CreatedUtc, TotalBytes, FileCount, FolderCount, UnreadableFolderCount)
            VALUES ($root, $created, $bytes, $files, $folders, $unreadable);
            SELECT last_insert_rowid();
            """;
        insertSnapshot.Parameters.AddWithValue("$root", record.RootPath);
        insertSnapshot.Parameters.AddWithValue("$created", record.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        insertSnapshot.Parameters.AddWithValue("$bytes", record.TotalBytes);
        insertSnapshot.Parameters.AddWithValue("$files", record.FileCount);
        insertSnapshot.Parameters.AddWithValue("$folders", record.FolderCount);
        insertSnapshot.Parameters.AddWithValue("$unreadable", record.UnreadableFolderCount);
        var id = (long)insertSnapshot.ExecuteScalar()!;

        const string InsertFolderSql = "INSERT OR REPLACE INTO SnapshotFolders VALUES ($id, $path, $bytes, $files)";
        using (var insertFolder = Prepare(connection, transaction, InsertFolderSql, "$id", "$path", "$bytes", "$files"))
        {
            foreach (var folder in record.Folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Bind(insertFolder, id, folder.Path, folder.Bytes, folder.FileCount);
            }
        }

        const string InsertFileSql = "INSERT OR REPLACE INTO SnapshotFiles VALUES ($id, $path, $bytes, $written)";
        using (var insertFile = Prepare(connection, transaction, InsertFileSql, "$id", "$path", "$bytes", "$written"))
        {
            foreach (var file in record.LargeFiles)
            {
                Bind(insertFile, id, file.Path, file.Bytes, file.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture));
            }
        }

        transaction.Commit();
        return new StorageSnapshot(
            id,
            record.RootPath,
            record.CreatedUtc,
            record.TotalBytes,
            record.FileCount,
            record.FolderCount,
            record.UnreadableFolderCount);
    }, cancellationToken);

    public Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<StorageSnapshot>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SnapshotColumns} FROM Snapshots ORDER BY Id DESC";
            return ReadSnapshots(command);
        }, cancellationToken);

    public Task<StorageSnapshot?> GetSnapshotAsync(long id, CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SnapshotColumns} FROM Snapshots WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return ReadSnapshots(command).FirstOrDefault();
    }, cancellationToken);

    public Task<StorageSnapshot?> GetPreviousAsync(
        string rootPath,
        long beforeId,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SnapshotColumns} FROM Snapshots WHERE RootPath = $root AND Id < $id ORDER BY Id DESC LIMIT 1";
        command.Parameters.AddWithValue("$root", rootPath);
        command.Parameters.AddWithValue("$id", beforeId);
        return ReadSnapshots(command).FirstOrDefault();
    }, cancellationToken);

    public Task<IReadOnlyList<FolderChange>> GetFolderChangesAsync(
        long beforeId,
        long afterId,
        CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<FolderChange>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.Path, COALESCE(b.Bytes, 0), a.Bytes
                FROM SnapshotFolders a
                LEFT JOIN SnapshotFolders b ON b.SnapshotId = $before AND b.Path = a.Path
                WHERE a.SnapshotId = $after AND a.Bytes <> COALESCE(b.Bytes, 0)
                UNION ALL
                SELECT b.Path, b.Bytes, 0
                FROM SnapshotFolders b
                WHERE b.SnapshotId = $before
                  AND NOT EXISTS (SELECT 1 FROM SnapshotFolders a WHERE a.SnapshotId = $after AND a.Path = b.Path)
                """;
            command.Parameters.AddWithValue("$before", beforeId);
            command.Parameters.AddWithValue("$after", afterId);

            using var reader = command.ExecuteReader();
            var changes = new List<FolderChange>();
            while (reader.Read())
            {
                changes.Add(new FolderChange(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
            }

            return changes;
        }, cancellationToken);

    public Task<IReadOnlyList<LargeFileChange>> GetLargeFileChangesAsync(
        long beforeId,
        long afterId,
        CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<LargeFileChange>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.Path, COALESCE(b.Bytes, 0), a.Bytes, a.LastWriteUtc
                FROM SnapshotFiles a
                LEFT JOIN SnapshotFiles b ON b.SnapshotId = $before AND b.Path = a.Path
                WHERE a.SnapshotId = $after AND a.Bytes <> COALESCE(b.Bytes, 0)
                ORDER BY a.Bytes - COALESCE(b.Bytes, 0) DESC
                """;
            command.Parameters.AddWithValue("$before", beforeId);
            command.Parameters.AddWithValue("$after", afterId);

            using var reader = command.ExecuteReader();
            var changes = new List<LargeFileChange>();
            while (reader.Read())
            {
                var lastWriteUtc = ParseUtc(reader.GetString(3));
                changes.Add(new LargeFileChange(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), lastWriteUtc));
            }

            return changes;
        }, cancellationToken);

    public Task PruneAsync(string rootPath, int keep, CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Snapshots
            WHERE RootPath = $root
              AND Id NOT IN (SELECT Id FROM Snapshots WHERE RootPath = $root ORDER BY Id DESC LIMIT $keep)
            """;
        command.Parameters.AddWithValue("$root", rootPath);
        command.Parameters.AddWithValue("$keep", keep);
        return command.ExecuteNonQuery();
    }, cancellationToken);

    public Task DeleteAllAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        Execute(connection, "DELETE FROM Snapshots; VACUUM;");
        return true;
    }, cancellationToken);

    private Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, "PRAGMA foreign_keys = ON;");
            return work(connection);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or FormatException)
        {
            // Callers see one exception type and never depend on SQLite.
            throw new HistoryStoreException(exception.Message, exception);
        }
    }, cancellationToken);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Prepare(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameters)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        command.Prepare();
        return command;
    }

    private static void Bind(SqliteCommand command, params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters[i].Value = values[i];
        }

        command.ExecuteNonQuery();
    }

    private static List<StorageSnapshot> ReadSnapshots(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var snapshots = new List<StorageSnapshot>();
        while (reader.Read())
        {
            snapshots.Add(new StorageSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseUtc(reader.GetString(2)),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6)));
        }

        return snapshots;
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
