using System.IO.Compression;
using System.Text.Json.Serialization;

namespace ClonarDC.Services;

public sealed class BackupService
{
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumManifestBytes = 512L * 1024;
    private const long MaximumSnapshotBytes = 64L * 1024 * 1024;

    public string BackupDirectory { get; }

    public BackupService()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        BackupDirectory = Path.Combine(documents, "GuildSync", "Backups");
        Directory.CreateDirectory(BackupDirectory);
        MigrateLegacyBackups(Path.Combine(documents, "Clonar DC", "Backups"));
    }

    public async Task<string> SaveAsync(
        GuildSnapshot snapshot,
        string? displayName = null,
        string? description = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions);
        if (payload.LongLength > MaximumSnapshotBytes)
            throw new InvalidDataException("O snapshot excedeu o limite seguro de tamanho.");

        var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        var envelope = new BackupEnvelope
        {
            FormatVersion = 2,
            Name = string.IsNullOrWhiteSpace(displayName)
                ? $"{snapshot.Name} — {DateTime.Now:yyyy-MM-dd HH-mm}"
                : displayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Tags = NormalizeTags(tags),
            PayloadSha256 = payloadHash,
            PayloadBytes = payload.LongLength,
            Snapshot = snapshot
        };

        var manifest = BackupManifestV2.FromEnvelope(envelope);
        var safeName = SafeFileName(envelope.Name);
        var path = Path.Combine(BackupDirectory, $"{safeName}-{envelope.Id[..8]}.cdbak");
        var temporary = path + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             65536,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestJsonOptions, ct);

                var snapshotEntry = zip.CreateEntry(manifest.SnapshotEntry, CompressionLevel.Optimal);
                await using (var snapshotStream = snapshotEntry.Open())
                    await snapshotStream.WriteAsync(payload, ct);
            }

            File.Move(temporary, path, true);
            return path;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public async Task<BackupEnvelope> LoadAsync(string path, CancellationToken ct = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Backup não encontrado.", path);
        if (file.Length <= 0 || file.Length > MaximumArchiveBytes)
            throw new InvalidDataException("O backup está vazio ou excede o limite seguro de tamanho.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (zip.Entries.Count > 8)
            throw new InvalidDataException("O backup contém uma quantidade inesperada de entradas.");

        var manifestEntry = zip.GetEntry("manifest.json")
                            ?? throw new InvalidDataException("Backup sem manifest.json.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
            throw new InvalidDataException("Manifesto do backup inválido.");

        var manifestBytes = await ReadEntryAsync(manifestEntry, MaximumManifestBytes, ct);
        var version = ReadFormatVersion(manifestBytes);
        return version <= 1
            ? LoadLegacyEnvelope(manifestBytes)
            : await LoadVersionTwoAsync(zip, manifestBytes, ct);
    }

    public IReadOnlyList<string> ListBackups() => Directory.Exists(BackupDirectory)
        ? Directory.EnumerateFiles(BackupDirectory, "*.cdbak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList()
        : [];

    private static BackupEnvelope LoadLegacyEnvelope(byte[] manifestBytes)
    {
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(manifestBytes, LegacyJsonOptions)
                       ?? throw new InvalidDataException("Backup legado inválido.");
        if (envelope.Snapshot is null)
            throw new InvalidDataException("Backup legado sem snapshot.");

        ValidateSnapshot(envelope.Snapshot);
        var payload = JsonSerializer.Serialize(envelope.Snapshot, LegacyJsonOptions);
        VerifyHash(Encoding.UTF8.GetBytes(payload), envelope.PayloadSha256);
        envelope.FormatVersion = Math.Max(1, envelope.FormatVersion);
        return envelope;
    }

    private static async Task<BackupEnvelope> LoadVersionTwoAsync(
        ZipArchive zip,
        byte[] manifestBytes,
        CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<BackupManifestV2>(manifestBytes, ManifestJsonOptions)
                       ?? throw new InvalidDataException("Manifesto do backup inválido.");
        if (manifest.FormatVersion != 2)
            throw new InvalidDataException($"Versão de backup não suportada: {manifest.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.SnapshotEntry) || manifest.SnapshotEntry.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Entrada de snapshot inválida.");

        var snapshotEntry = zip.GetEntry(manifest.SnapshotEntry)
                            ?? throw new InvalidDataException("Backup sem snapshot.json.");
        if (snapshotEntry.Length <= 0 || snapshotEntry.Length > MaximumSnapshotBytes)
            throw new InvalidDataException("Snapshot do backup inválido ou demasiado grande.");
        if (manifest.PayloadBytes > 0 && snapshotEntry.Length != manifest.PayloadBytes)
            throw new InvalidDataException("O tamanho do snapshot não corresponde ao manifesto.");

        var payload = await ReadEntryAsync(snapshotEntry, MaximumSnapshotBytes, ct);
        VerifyHash(payload, manifest.PayloadSha256);
        var snapshot = JsonSerializer.Deserialize<GuildSnapshot>(payload, SnapshotJsonOptions)
                       ?? throw new InvalidDataException("Snapshot do backup inválido.");
        ValidateSnapshot(snapshot);

        return new BackupEnvelope
        {
            FormatVersion = manifest.FormatVersion,
            Id = manifest.Id,
            CreatedAt = manifest.CreatedAt,
            AppVersion = manifest.AppVersion,
            Name = manifest.Name,
            Description = manifest.Description,
            Tags = manifest.Tags ?? [],
            SnapshotEntry = manifest.SnapshotEntry,
            PayloadSha256 = manifest.PayloadSha256,
            PayloadBytes = payload.LongLength,
            Protection = manifest.Protection,
            Snapshot = snapshot
        };
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken ct)
    {
        await using var source = entry.Open();
        await using var destination = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("A entrada compactada excedeu o limite seguro.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return destination.ToArray();
    }

    private static int ReadFormatVersion(byte[] manifestBytes)
    {
        try
        {
            return JsonNode.Parse(manifestBytes)?["formatVersion"]?.GetValue<int>() ?? 1;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("Manifesto do backup não contém JSON válido.", exception);
        }
    }

    private static void VerifyHash(byte[] payload, string expectedText)
    {
        if (string.IsNullOrWhiteSpace(expectedText) || expectedText.Length != 64)
            throw new InvalidDataException("Hash de integridade ausente ou inválido.");
        byte[] expected;
        try { expected = Convert.FromHexString(expectedText); }
        catch (FormatException exception) { throw new InvalidDataException("Hash de integridade inválido.", exception); }
        var actual = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidDataException("Falha de integridade: o backup foi alterado ou corrompido.");
    }

    private static void ValidateSnapshot(GuildSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < 1 or > 2)
            throw new InvalidDataException($"Versão de snapshot não suportada: {snapshot.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(snapshot.SourceGuildId) || !snapshot.SourceGuildId.All(char.IsDigit))
            throw new InvalidDataException("O snapshot não contém um servidor de origem válido.");
        if (string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Length > 100)
            throw new InvalidDataException("O snapshot contém um nome de servidor inválido.");
        if (snapshot.Roles.Count > 1000 || snapshot.Channels.Count > 5000 || snapshot.Emojis.Count > 1000)
            throw new InvalidDataException("O snapshot excede os limites estruturais de segurança.");
    }

    private void MigrateLegacyBackups(string legacyDirectory)
    {
        if (!Directory.Exists(legacyDirectory) ||
            string.Equals(legacyDirectory, BackupDirectory, StringComparison.OrdinalIgnoreCase)) return;

        foreach (var legacyPath in Directory.EnumerateFiles(legacyDirectory, "*.cdbak", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var destination = Path.Combine(BackupDirectory, Path.GetFileName(legacyPath));
                if (!File.Exists(destination)) File.Copy(legacyPath, destination, false);
            }
            catch
            {
                // Migration is best effort; original files remain untouched.
            }
        }
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags) => tags?
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Where(value => value.Length <= 40)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(20)
        .ToList() ?? [];

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Backup";
        return cleaned[..Math.Min(cleaned.Length, 80)];
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class BackupManifestV2
    {
        public int FormatVersion { get; set; } = 2;
        public string Id { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string AppVersion { get; set; } = "0.8.1";
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
        public string SnapshotEntry { get; set; } = "snapshot.json";
        public string PayloadSha256 { get; set; } = string.Empty;
        public long PayloadBytes { get; set; }
        public string Protection { get; set; } = "integrity-only";

        public static BackupManifestV2 FromEnvelope(BackupEnvelope envelope) => new()
        {
            FormatVersion = 2,
            Id = envelope.Id,
            CreatedAt = envelope.CreatedAt,
            AppVersion = envelope.AppVersion,
            Name = envelope.Name,
            Description = envelope.Description,
            Tags = envelope.Tags,
            SnapshotEntry = envelope.SnapshotEntry,
            PayloadSha256 = envelope.PayloadSha256,
            PayloadBytes = envelope.PayloadBytes,
            Protection = envelope.Protection
        };
    }

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions LegacyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
