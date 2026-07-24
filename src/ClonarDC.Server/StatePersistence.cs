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
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
            return new PostgresStatePersistence(databaseUrl);

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
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<Database>(json, options);
    }

    public async Task SaveAsync(Database database, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(database, options), cancellationToken);
        File.Move(temporaryPath, path, true);
    }
}

sealed class PostgresStatePersistence : IStatePersistence
{
    private readonly string _connectionString;
    private bool _schemaReady;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    public PostgresStatePersistence(string databaseUrl) => _connectionString = NormalizeConnectionString(databaseUrl);

    public string Kind => "postgresql";

    public async Task<Database?> LoadAsync(JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT payload::text FROM clonardc_state WHERE id = 1", connection);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<Database>(payload, options);
    }

    public async Task SaveAsync(Database database, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(database, options);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO clonardc_state (id, payload, updated_at)
            VALUES (1, @payload, now())
            ON CONFLICT (id) DO UPDATE
            SET payload = EXCLUDED.payload, updated_at = now()
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                    updated_at timestamptz NOT NULL DEFAULT now()
                )
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
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Pooling = true,
            MaxPoolSize = 20,
            Timeout = 15,
            CommandTimeout = 30,
            SslMode = SslMode.Prefer
        };
        return builder.ConnectionString;
    }
}