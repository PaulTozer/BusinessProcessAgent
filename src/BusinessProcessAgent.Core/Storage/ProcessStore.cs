using BusinessProcessAgent.Core.Models;

namespace BusinessProcessAgent.Core.Storage;

/// <summary>
/// SQLite-backed storage for observation sessions, process steps, and
/// assembled business processes. Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class ProcessStore : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<ProcessStore> _logger;

    public ProcessStore(string dbPath, ILogger<ProcessStore> logger)
    {
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS observation_sessions (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                label TEXT,
                step_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS process_steps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                application_name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                high_level_action TEXT NOT NULL,
                low_level_action TEXT NOT NULL,
                user_intent TEXT NOT NULL,
                business_process_name TEXT,
                step_number INTEGER NOT NULL DEFAULT 0,
                screenshot_path TEXT,
                additional_context TEXT,
                confidence REAL NOT NULL DEFAULT 0.0,
                FOREIGN KEY (session_id) REFERENCES observation_sessions(id)
            );

            CREATE INDEX IF NOT EXISTS idx_steps_session ON process_steps(session_id);
            CREATE INDEX IF NOT EXISTS idx_steps_timestamp ON process_steps(timestamp);
            CREATE INDEX IF NOT EXISTS idx_steps_process ON process_steps(business_process_name);

            CREATE TABLE IF NOT EXISTS business_processes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                description TEXT,
                first_seen TEXT NOT NULL,
                last_seen TEXT,
                times_observed INTEGER NOT NULL DEFAULT 1
            );
            """;
        cmd.ExecuteNonQuery();
        _logger.LogInformation("ProcessStore schema ensured at {Path}", _connectionString);
    }

    public async Task StartSessionAsync(ObservationSession session)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO observation_sessions (id, started_at, label)
                VALUES (@id, @startedAt, @label)
                """;
            cmd.Parameters.AddWithValue("@id", session.Id);
            cmd.Parameters.AddWithValue("@startedAt", session.StartedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@label", (object?)session.Label ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _semaphore.Release(); }
    }

    public async Task EndSessionAsync(string sessionId, DateTimeOffset endedAt, int stepCount)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE observation_sessions
                SET ended_at = @endedAt, step_count = @stepCount
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", sessionId);
            cmd.Parameters.AddWithValue("@endedAt", endedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@stepCount", stepCount);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _semaphore.Release(); }
    }

    public async Task<long> InsertStepAsync(ProcessStep step)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO process_steps
                    (session_id, timestamp, application_name, window_title,
                     high_level_action, low_level_action, user_intent,
                     business_process_name, step_number, screenshot_path,
                     additional_context, confidence)
                VALUES
                    (@sessionId, @timestamp, @appName, @windowTitle,
                     @highLevel, @lowLevel, @intent,
                     @processName, @stepNumber, @screenshotPath,
                     @additionalContext, @confidence);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@sessionId", step.SessionId);
            cmd.Parameters.AddWithValue("@timestamp", step.Timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@appName", step.ApplicationName);
            cmd.Parameters.AddWithValue("@windowTitle", step.WindowTitle);
            cmd.Parameters.AddWithValue("@highLevel", step.HighLevelAction);
            cmd.Parameters.AddWithValue("@lowLevel", step.LowLevelAction);
            cmd.Parameters.AddWithValue("@intent", step.UserIntent);
            cmd.Parameters.AddWithValue("@processName", (object?)step.BusinessProcessName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@stepNumber", step.StepNumber);
            cmd.Parameters.AddWithValue("@screenshotPath", (object?)step.ScreenshotPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@additionalContext", (object?)step.AdditionalContext ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@confidence", step.Confidence);
            var id = (long)(await cmd.ExecuteScalarAsync())!;
            return id;
        }
        finally { _semaphore.Release(); }
    }

    public async Task<IReadOnlyList<ProcessStep>> GetStepsBySessionAsync(string sessionId)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM process_steps
                WHERE session_id = @sessionId
                ORDER BY timestamp ASC
                """;
            cmd.Parameters.AddWithValue("@sessionId", sessionId);

            var steps = new List<ProcessStep>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                steps.Add(ReadStep(reader));
            }
            return steps;
        }
        finally { _semaphore.Release(); }
    }

    public async Task<IReadOnlyList<ProcessStep>> GetRecentStepsAsync(int take = 50)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT * FROM process_steps
                ORDER BY timestamp DESC
                LIMIT {take}
                """;

            var steps = new List<ProcessStep>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                steps.Add(ReadStep(reader));
            }
            steps.Reverse();
            return steps;
        }
        finally { _semaphore.Release(); }
    }

    public async Task<IReadOnlyList<ObservationSession>> GetSessionsAsync(int take = 20)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT * FROM observation_sessions
                ORDER BY started_at DESC
                LIMIT {take}
                """;

            var sessions = new List<ObservationSession>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add(new ObservationSession
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    StartedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
                    EndedAt = reader.IsDBNull(reader.GetOrdinal("ended_at"))
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ended_at"))),
                    Label = reader.IsDBNull(reader.GetOrdinal("label")) ? null : reader.GetString(reader.GetOrdinal("label")),
                    StepCount = reader.GetInt32(reader.GetOrdinal("step_count")),
                });
            }
            return sessions;
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Deletes a single session and all its steps. Returns screenshot paths
    /// so the caller can clean up files.
    /// </summary>
    public async Task<IReadOnlyList<string>> DeleteSessionAsync(string sessionId)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Collect screenshot paths before deleting
            var paths = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT screenshot_path FROM process_steps WHERE session_id = @id AND screenshot_path IS NOT NULL";
                cmd.Parameters.AddWithValue("@id", sessionId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    paths.Add(reader.GetString(0));
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM process_steps WHERE session_id = @id";
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM observation_sessions WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Deleted session {SessionId} ({Screenshots} screenshots)", sessionId, paths.Count);
            return paths;
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Deletes ALL sessions, steps, and returns all screenshot paths for cleanup.
    /// </summary>
    public async Task<IReadOnlyList<string>> ClearAllAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var paths = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT screenshot_path FROM process_steps WHERE screenshot_path IS NOT NULL";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    paths.Add(reader.GetString(0));
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM process_steps; DELETE FROM observation_sessions; DELETE FROM business_processes;";
                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Cleared all data ({Screenshots} screenshots)", paths.Count);
            return paths;
        }
        finally { _semaphore.Release(); }
    }

    private static ProcessStep ReadStep(SqliteDataReader reader)
    {
        return new ProcessStep
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            Timestamp = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
            ApplicationName = reader.GetString(reader.GetOrdinal("application_name")),
            WindowTitle = reader.GetString(reader.GetOrdinal("window_title")),
            HighLevelAction = reader.GetString(reader.GetOrdinal("high_level_action")),
            LowLevelAction = reader.GetString(reader.GetOrdinal("low_level_action")),
            UserIntent = reader.GetString(reader.GetOrdinal("user_intent")),
            BusinessProcessName = reader.IsDBNull(reader.GetOrdinal("business_process_name"))
                ? null : reader.GetString(reader.GetOrdinal("business_process_name")),
            StepNumber = reader.GetInt32(reader.GetOrdinal("step_number")),
            ScreenshotPath = reader.IsDBNull(reader.GetOrdinal("screenshot_path"))
                ? null : reader.GetString(reader.GetOrdinal("screenshot_path")),
            AdditionalContext = reader.IsDBNull(reader.GetOrdinal("additional_context"))
                ? null : reader.GetString(reader.GetOrdinal("additional_context")),
            Confidence = reader.GetDouble(reader.GetOrdinal("confidence")),
        };
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
