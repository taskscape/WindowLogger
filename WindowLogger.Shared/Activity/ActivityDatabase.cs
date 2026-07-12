using Microsoft.Data.Sqlite;

namespace WindowLogger.Shared.Activity;

public sealed class ActivityDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public ActivityDatabase(string path, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!readOnly)
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false
        };
        _connection = new SqliteConnection(connectionString.ToString());
        _connection.Open();

        ExecuteNonQuery("PRAGMA busy_timeout = 5000;");
        if (!readOnly)
        {
            ExecuteNonQuery("PRAGMA journal_mode = WAL;");
            ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
            InitializeSchema();
        }
    }

    public long Count
    {
        get
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM activity_events;";
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    public void Append(ActivityRecord record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using SqliteCommand command = CreateInsertCommand(transaction: null);
        BindRecord(command, record);
        command.ExecuteNonQuery();
    }

    public int Import(IEnumerable<ActivityRecord> records, bool replace = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using SqliteTransaction transaction = _connection.BeginTransaction();
        if (replace)
        {
            using SqliteCommand clearCommand = _connection.CreateCommand();
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM activity_events;";
            clearCommand.ExecuteNonQuery();
        }

        int count = 0;
        using SqliteCommand insertCommand = CreateInsertCommand(transaction);
        foreach (ActivityRecord record in records)
        {
            BindRecord(insertCommand, record);
            insertCommand.ExecuteNonQuery();
            count++;
        }

        if (replace && count == 0)
        {
            throw new InvalidDataException("The import contained no valid activity records; the existing database was not changed.");
        }

        transaction.Commit();
        return count;
    }

    public IEnumerable<ActivityRecord> Read(DateTime? from = null, DateTime? to = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT timestamp_ticks, window_title, status
            FROM activity_events
            WHERE ($from IS NULL OR timestamp_ticks >= $from)
              AND ($to IS NULL OR timestamp_ticks < $to)
            ORDER BY timestamp_ticks, id;
            """;
        command.Parameters.AddWithValue("$from", from?.Ticks is long fromTicks ? fromTicks : DBNull.Value);
        command.Parameters.AddWithValue("$to", to?.Ticks is long toTicks ? toTicks : DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DateTime timestamp = new(reader.GetInt64(0), DateTimeKind.Unspecified);
            yield return ActivityRecord.Create(timestamp, reader.GetString(1), reader.GetString(2));
        }

        transaction.Commit();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private void InitializeSchema()
    {
        ExecuteNonQuery("""
            CREATE TABLE IF NOT EXISTS activity_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_ticks INTEGER NOT NULL,
                window_title TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('Active', 'Inactive', 'SessionStart', 'SessionEnd'))
            );
            CREATE INDEX IF NOT EXISTS ix_activity_events_timestamp
                ON activity_events(timestamp_ticks, id);
            """);
    }

    private SqliteCommand CreateInsertCommand(SqliteTransaction? transaction)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity_events (timestamp_ticks, window_title, status)
            VALUES ($timestamp, $windowTitle, $status);
            """;
        command.Parameters.Add("$timestamp", SqliteType.Integer);
        command.Parameters.Add("$windowTitle", SqliteType.Text);
        command.Parameters.Add("$status", SqliteType.Text);
        return command;
    }

    private static void BindRecord(SqliteCommand command, ActivityRecord record)
    {
        command.Parameters["$timestamp"].Value = record.Timestamp.Ticks;
        command.Parameters["$windowTitle"].Value = record.WindowTitle;
        command.Parameters["$status"].Value = record.Status;
    }

    private void ExecuteNonQuery(string commandText)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
