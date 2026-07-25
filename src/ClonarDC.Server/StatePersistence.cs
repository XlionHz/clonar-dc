using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

interface IStatePersistence
{
    string Kind { get; }
    Task<Database?> LoadAsync(JsonSerializerOptions options, CancellationToken cancellationToken = default);
    Task SaveAsync(Database database, JsonSerializerOptions options, CancellationToken cancellationToken = default);
}

static class StatePersistenceFactory
{
    public static IStatePersistence Create(string dataRoot)
    {
        var databaseUrl = Environment.GetEnvironmentVariable("GUILDSYNC_DATABASE_URL")
                          ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
            return new PostgresStatePersistence(databaseUrl);

        var environment = Environment.GetEnvironmentVariable("GUILDSYNC_ENV")
                          ?? Environment.GetEnvironmentVariable("CLONARDC_ENV");
        var production = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);
        var allowFileStorage = string.Equals(
            Environment.GetEnvironmentVariable("GUILDSYNC_ALLOW_FILE_STORAGE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (production && !allowFileStorage)
            throw new InvalidOperationException(
                "Production requires DATABASE_URL. Set GUILDSYNC_ALLOW_FILE_STORAGE=true only for an intentional single-machine deployment.");

        Directory.CreateDirectory(dataRoot);
        return new FileStatePersistence(Path.Combine(dataRoot, "store.json"));
    }
}

sealed class FileStatePersistence(string path) : IStatePersistence
{
    public string Kind => "file";

    public async Task<Database?> LoadAsync(JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<Database>(stream, options, cancellationToken);
    }

    public async Task SaveAsync(Database database, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, database, options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path)) File.Copy(path, backupPath, true);
        File.Move(temporaryPath, path, true);
    }
}

sealed class PostgresStatePersistence : IStatePersistence
{
    private const long AdvisoryLockId = 0x4753594E43; // "GSYNC"
    private readonly string _connectionString;
    private bool _schemaReady;
    private long _knownRevision;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    public PostgresStatePersistence(string databaseUrl) => _connectionString = NormalizeConnectionString(databaseUrl);

    public string Kind => "postgresql";

    public async Task<Database?> LoadAsync(JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT payload::text, revision FROM clonardc_state WHERE id = 1",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            _knownRevision = 0;
            return null;
        }

        var payload = reader.GetString(0);
        _knownRevision = reader.GetInt64(1);
        return string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<Database>(payload, options);
    }

    public async Task SaveAsync(Database database, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(database, options);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(@lock_id)",
                         connection,
                         transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_id", AdvisoryLockId);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        long currentRevision;
        await using (var revisionCommand = new NpgsqlCommand(
                         "SELECT revision FROM clonardc_state WHERE id = 1 FOR UPDATE",
                         connection,
                         transaction))
        {
            var result = await revisionCommand.ExecuteScalarAsync(cancellationToken);
            currentRevision = result is null or DBNull ? 0 : Convert.ToInt64(result);
        }

        if (_knownRevision != currentRevision)
            throw new InvalidOperationException(
                "GuildSync detected a concurrent database writer and refused to overwrite newer state. Run a single API instance or migrate to normalized storage.");

        var nextRevision = currentRevision + 1;
        await using (var command = new NpgsqlCommand(
                         """
                         INSERT INTO clonardc_state (id, payload, revision, updated_at)
                         VALUES (1, @payload, @revision, now())
                         ON CONFLICT (id) DO UPDATE
                         SET payload = EXCLUDED.payload,
                             revision = EXCLUDED.revision,
                             updated_at = now()
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
            command.Parameters.AddWithValue("revision", nextRevision);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _knownRevision = nextRevision;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS clonardc_state (
                    id integer PRIMARY KEY CHECK (id = 1),
                    payload jsonb NOT NULL,
                    revision bigint NOT NULL DEFAULT 1,
                    updated_at timestamptz NOT NULL DEFAULT now()
                );
                ALTER TABLE clonardc_state
                    ADD COLUMN IF NOT EXISTS revision bigint NOT NULL DEFAULT 1;
                """,
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static string NormalizeConnectionString(string raw)
    {
        raw = raw.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal)) return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var environment = Environment.GetEnvironmentVariable("GUILDSYNC_ENV")
                          ?? Environment.GetEnvironmentVariable("CLONARDC_ENV");
        var production = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 20,
            KeepAlive = 30,
            Timeout = 15,
            CommandTimeout = 30,
            SslMode = production ? SslMode.Require : SslMode.Prefer
        };
        return builder.ConnectionString;
    }
}
