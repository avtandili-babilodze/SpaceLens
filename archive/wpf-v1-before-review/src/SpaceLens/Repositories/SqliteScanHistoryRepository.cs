using System.IO;
using Microsoft.Data.Sqlite;
using SpaceLens.Infrastructure;
using SpaceLens.Models;
using SpaceLens.Services.Scanning;

namespace SpaceLens.Repositories;

public sealed class SqliteScanHistoryRepository(AppPaths paths) : IScanHistoryRepository
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false, DefaultTimeout = 30 }.ToString());
        connection.Open();
        return connection;
    }

    private Task<T> Run<T>(Func<SqliteConnection, T> action) => Task.Run(() => { using var db = Open(); return action(db); });
    internal static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    public Task InitializeAsync() => Run(db =>
    {
        using var command = Command(db, """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS Snapshots(
                Id INTEGER PRIMARY KEY, RootPath TEXT NOT NULL, CreatedUtc TEXT NOT NULL, Status INTEGER NOT NULL,
                Size INTEGER NOT NULL DEFAULT 0, Files INTEGER NOT NULL DEFAULT 0, Folders INTEGER NOT NULL DEFAULT 0,
                Errors INTEGER NOT NULL DEFAULT 0, Skipped INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Entries(
                Id INTEGER PRIMARY KEY, SnapshotId INTEGER NOT NULL, ParentId INTEGER, FullPath TEXT NOT NULL, Name TEXT NOT NULL,
                IsDirectory INTEGER NOT NULL, IsReparsePoint INTEGER NOT NULL, Size INTEGER NOT NULL, FileCount INTEGER NOT NULL,
                FolderCount INTEGER NOT NULL, ModifiedUtc TEXT, Extension TEXT NOT NULL, Category TEXT NOT NULL, Risk INTEGER NOT NULL,
                Reason TEXT NOT NULL, Warning TEXT);
            CREATE INDEX IF NOT EXISTS IX_Entries_Parent ON Entries(SnapshotId,ParentId,IsDirectory);
            CREATE INDEX IF NOT EXISTS IX_Entries_Size ON Entries(SnapshotId,IsDirectory,Size DESC);
            CREATE INDEX IF NOT EXISTS IX_Entries_Path ON Entries(SnapshotId,FullPath COLLATE NOCASE);
            UPDATE Snapshots SET Status=3 WHERE Status=0;
            """);
        command.ExecuteNonQuery(); return true;
    });

    public IScanWriter CreateScan(string root) => new ScanWriter(Open(), root);
    private static StorageSnapshot ReadSnapshot(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind), (ScanStatus)r.GetInt32(3), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.GetInt64(8));
    internal static FileEntry ReadEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SnapshotId = r.GetInt64(1),
        ParentId = r.IsDBNull(2) ? null : r.GetInt64(2),
        FullPath = r.GetString(3),
        Name = r.GetString(4),
        IsDirectory = r.GetBoolean(5),
        IsReparsePoint = r.GetBoolean(6),
        Size = r.GetInt64(7),
        FileCount = r.GetInt64(8),
        FolderCount = r.GetInt64(9),
        ModifiedUtc = r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Extension = r.GetString(11),
        Category = r.GetString(12),
        Risk = (CleanupRisk)r.GetInt32(13),
        Reason = r.GetString(14),
        Warning = r.IsDBNull(15) ? null : r.GetString(15)
    };
    private static List<FileEntry> ReadEntries(SqliteCommand command)
    {
        using var reader = command.ExecuteReader(); var entries = new List<FileEntry>();
        while (reader.Read()) entries.Add(ReadEntry(reader)); return entries;
    }
    private static StorageSnapshot? FindSnapshot(SqliteConnection db, long id)
    {
        using var cmd = Command(db, "SELECT * FROM Snapshots WHERE Id=$id", ("$id", id));
        using var reader = cmd.ExecuteReader(); return reader.Read() ? ReadSnapshot(reader) : null;
    }
    public Task<IReadOnlyList<StorageSnapshot>> GetSnapshotsAsync() => Run<IReadOnlyList<StorageSnapshot>>(db =>
    {
        using var cmd = Command(db, "SELECT * FROM Snapshots ORDER BY Id DESC LIMIT 100");
        using var reader = cmd.ExecuteReader(); var result = new List<StorageSnapshot>();
        while (reader.Read()) result.Add(ReadSnapshot(reader)); return result;
    });
    public Task<StorageSnapshot?> GetSnapshotAsync(long id) => Run(db => FindSnapshot(db, id));
    public Task<FileEntry?> GetEntryAsync(long id) => Run(db => { using var cmd = Command(db, "SELECT * FROM Entries WHERE Id=$id", ("$id", id)); return ReadEntries(cmd).FirstOrDefault(); });
    public Task<FileEntry?> GetRootAsync(long snapshotId) => Run(db => { using var cmd = Command(db, "SELECT * FROM Entries WHERE SnapshotId=$id AND ParentId IS NULL LIMIT 1", ("$id", snapshotId)); return ReadEntries(cmd).FirstOrDefault(); });
    public Task<FileEntry?> FindPathAsync(long snapshotId, string path) => Run(db => { using var cmd = Command(db, "SELECT * FROM Entries WHERE SnapshotId=$id AND FullPath=$path COLLATE NOCASE LIMIT 1", ("$id", snapshotId), ("$path", path)); return ReadEntries(cmd).FirstOrDefault(); });
    internal static string Prefix(string path) => path.TrimEnd('\\', '/').Replace("|", "||").Replace("%", "|%").Replace("_", "|_") + Path.DirectorySeparatorChar + "%";

    public Task<EntryPage> GetChildrenAsync(EntryQuery query) => Run(db =>
    {
        var sort = query.Sort switch { "Name" => "Name COLLATE NOCASE", "Type" => "Category", "Modified" => "ModifiedUtc", "Risk" => "Risk", _ => "Size" };
        var filter = query.Filter switch { "Folders" => "AND IsDirectory=1", "Files" => "AND IsDirectory=0", "Large files (≥1 GiB)" => "AND IsDirectory=0 AND Size>=1073741824", "Review required" => "AND IsDirectory=0 AND Risk=1", _ => "" };
        string scope = "SnapshotId=$snapshot AND ";
        if (query.Recursive)
        {
            using var parent = Command(db, "SELECT FullPath FROM Entries WHERE Id=$id AND SnapshotId=$snapshot", ("$id", query.ParentId), ("$snapshot", query.SnapshotId));
            string parentPath = parent.ExecuteScalar() as string ?? throw new InvalidOperationException("Selected folder is missing from this scan.");
            scope += "FullPath LIKE $scope ESCAPE '|'";
            return Query(Prefix(parentPath));
        }
        scope += "ParentId=$scope";
        return Query(query.ParentId);

        EntryPage Query(object scopeValue)
        {
            string where = scope + " AND Name LIKE $search ESCAPE '|' " + filter;
            string search = "%" + query.Search.Replace("|", "||").Replace("%", "|%").Replace("_", "|_") + "%";
            (string Name, object? Value)[] args = [("$snapshot", query.SnapshotId), ("$scope", scopeValue), ("$search", search)];
            using var count = Command(db, "SELECT COUNT(*) FROM Entries WHERE " + where, args);
            long total = (long)(count.ExecuteScalar() ?? 0L);
            using var cmd = Command(db, $"SELECT * FROM Entries WHERE {where} ORDER BY IsDirectory DESC,{sort} {(query.Descending ? "DESC" : "ASC")},Id LIMIT $limit OFFSET $offset", args);
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 1000)); cmd.Parameters.AddWithValue("$offset", Math.Max(query.Offset, 0));
            return new EntryPage(ReadEntries(cmd), total);
        }
    });

    public Task<FolderDetails> GetDetailsAsync(FileEntry entry) => Run(db =>
    {
        (string Name, object? Value)[] args = [("$snapshot", entry.SnapshotId), ("$path", entry.FullPath), ("$prefix", Prefix(entry.FullPath))];
        const string scope = "SnapshotId=$snapshot AND IsDirectory=0 AND (FullPath=$path OR FullPath LIKE $prefix ESCAPE '|')";
        using var categoriesCmd = Command(db, $"SELECT Category,SUM(Size),COUNT(*) FROM Entries WHERE {scope} GROUP BY Category ORDER BY SUM(Size) DESC", args);
        var categories = new List<CategoryUsage>();
        using (var r = categoriesCmd.ExecuteReader())
            while (r.Read()) categories.Add(new(r.GetString(0), r.GetInt64(1), r.GetInt64(2), entry.Size == 0 ? 0 : 100d * r.GetInt64(1) / entry.Size, FileCategories.Color(r.GetString(0))));
        using var largest = Command(db, $"SELECT * FROM Entries WHERE {scope} ORDER BY Size DESC LIMIT 6", args);
        var files = ReadEntries(largest);
        using var stats = Command(db, $"SELECT COALESCE(SUM(CASE WHEN Risk=1 THEN Size ELSE 0 END),0), COALESCE(SUM(CASE WHEN Size>=1073741824 THEN 1 ELSE 0 END),0) FROM Entries WHERE {scope}", args);
        using var reader = stats.ExecuteReader(); reader.Read();
        return new FolderDetails(entry, categories, files, reader.GetInt64(0), reader.GetInt64(1));
    });

    public Task<IReadOnlyList<FileEntry>> GetCandidatesAsync(long snapshotId, int limit = 500) => Run<IReadOnlyList<FileEntry>>(db =>
    {
        using var cmd = Command(db, "SELECT * FROM Entries WHERE SnapshotId=$id AND IsDirectory=0 AND IsReparsePoint=0 AND Risk IN (0,1) AND Size>0 ORDER BY Risk,CASE WHEN Category IN ('Archives','Applications') THEN 0 ELSE 1 END,Size DESC LIMIT $limit", ("$id", snapshotId), ("$limit", Math.Clamp(limit, 1, 5000)));
        return ReadEntries(cmd);
    });

    public Task<SnapshotComparison> CompareAsync(long beforeId, long afterId) => Run(db =>
    {
        var before = FindSnapshot(db, beforeId) ?? throw new InvalidOperationException("Earlier snapshot is missing.");
        var after = FindSnapshot(db, afterId) ?? throw new InvalidOperationException("Later snapshot is missing.");
        if (before.Id == after.Id || !string.Equals(before.RootPath, after.RootPath, StringComparison.OrdinalIgnoreCase) || before.Status != ScanStatus.Completed || after.Status != ScanStatus.Completed)
            throw new InvalidOperationException("Choose two different completed snapshots of the same location.");
        using var cmd = Command(db, """
            SELECT * FROM (
              SELECT n.FullPath,n.IsDirectory,COALESCE(o.Size,0) AS BeforeSize,n.Size AS AfterSize,
                CASE WHEN o.Id IS NULL THEN 'Newly discovered' ELSE 'Changed' END AS Kind
              FROM Entries n LEFT JOIN Entries o ON o.SnapshotId=$before AND o.FullPath=n.FullPath COLLATE NOCASE
              WHERE n.SnapshotId=$after AND (o.Id IS NULL OR n.Size!=o.Size)
              UNION ALL
              SELECT o.FullPath,o.IsDirectory,o.Size,0,'No longer found' FROM Entries o
              LEFT JOIN Entries n ON n.SnapshotId=$after AND n.FullPath=o.FullPath COLLATE NOCASE
              WHERE o.SnapshotId=$before AND n.Id IS NULL
            ) ORDER BY ABS(AfterSize-BeforeSize) DESC LIMIT 500
            """, ("$before", beforeId), ("$after", afterId));
        using var reader = cmd.ExecuteReader(); var changes = new List<StorageChange>();
        while (reader.Read()) changes.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4)));
        return new SnapshotComparison(before, after, changes);
    });

    public Task<IReadOnlyList<IReadOnlyList<FileEntry>>> GetDuplicateCandidatesAsync(long snapshotId) => Run<IReadOnlyList<IReadOnlyList<FileEntry>>>(db =>
    {
        using var cmd = Command(db, "SELECT * FROM Entries WHERE SnapshotId=$id AND IsDirectory=0 AND IsReparsePoint=0 AND Size>0 AND Size IN (SELECT Size FROM Entries WHERE SnapshotId=$id AND IsDirectory=0 AND IsReparsePoint=0 AND Size>0 GROUP BY Size HAVING COUNT(*)>1) ORDER BY Size DESC LIMIT 10000", ("$id", snapshotId));
        return ReadEntries(cmd).GroupBy(e => e.Size).Where(g => g.Count() > 1).Select(g => (IReadOnlyList<FileEntry>)g.ToArray()).ToArray();
    });

    private sealed class ScanWriter : IScanWriter
    {
        private readonly SqliteConnection db;
        private SqliteTransaction transaction;
        private readonly SqliteCommand insert;
        private int pending;
        private bool finished;
        public long SnapshotId { get; }

        public ScanWriter(SqliteConnection connection, string root)
        {
            db = connection;
            using var cmd = Command(db, "INSERT INTO Snapshots(RootPath,CreatedUtc,Status) VALUES($root,$created,0); SELECT last_insert_rowid();", ("$root", root), ("$created", DateTime.UtcNow.ToString("O")));
            SnapshotId = (long)cmd.ExecuteScalar()!;
            transaction = db.BeginTransaction();
            insert = Command(db, """
                INSERT INTO Entries(SnapshotId,ParentId,FullPath,Name,IsDirectory,IsReparsePoint,Size,FileCount,FolderCount,ModifiedUtc,Extension,Category,Risk,Reason,Warning)
                VALUES($snapshot,$parent,$path,$name,$directory,$reparse,$size,$files,$folders,$modified,$extension,$category,$risk,$reason,$warning);
                SELECT last_insert_rowid();
                """);
            foreach (string name in new[] { "$snapshot", "$parent", "$path", "$name", "$directory", "$reparse", "$size", "$files", "$folders", "$modified", "$extension", "$category", "$risk", "$reason", "$warning" }) insert.Parameters.Add(new SqliteParameter(name, DBNull.Value));
            insert.Transaction = transaction;
        }

        public long Insert(FileEntry e)
        {
            object?[] values = [SnapshotId, e.ParentId, e.FullPath, e.Name, e.IsDirectory, e.IsReparsePoint, e.Size, e.FileCount, e.FolderCount, e.ModifiedUtc?.ToString("O"), e.Extension, e.Category, (int)e.Risk, e.Reason, e.Warning];
            for (int i = 0; i < values.Length; i++) insert.Parameters[i].Value = values[i] ?? DBNull.Value;
            long id = (long)insert.ExecuteScalar()!; Batch(); return id;
        }
        private void Batch()
        {
            if (++pending < 512) return;
            transaction.Commit(); transaction.Dispose(); transaction = db.BeginTransaction(); insert.Transaction = transaction; pending = 0;
        }
        public void CompleteFolder(long id, long size, long files, long folders, string? warning)
        {
            using var cmd = Command(db, "UPDATE Entries SET Size=$size,FileCount=$files,FolderCount=$folders,Warning=$warning WHERE Id=$id", ("$size", size), ("$files", files), ("$folders", folders), ("$warning", warning), ("$id", id));
            cmd.Transaction = transaction; cmd.ExecuteNonQuery(); Batch();
        }
        public void Finish(ScanStatus status, ScanProgress progress)
        {
            using var cmd = Command(db, "UPDATE Snapshots SET Status=$status,Size=$size,Files=$files,Folders=$folders,Errors=$errors,Skipped=$skipped WHERE Id=$id", ("$status", (int)status), ("$size", progress.Bytes), ("$files", progress.Files), ("$folders", progress.Folders), ("$errors", progress.Errors), ("$skipped", progress.Skipped), ("$id", SnapshotId));
            cmd.Transaction = transaction; cmd.ExecuteNonQuery(); transaction.Commit(); finished = true;
        }
        public void Dispose()
        {
            if (!finished) { try { transaction.Commit(); } catch (SqliteException) { } }
            insert.Dispose(); transaction.Dispose(); db.Dispose();
        }
    }
}
