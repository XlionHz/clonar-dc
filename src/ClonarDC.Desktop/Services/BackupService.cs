using System.IO.Compression;

namespace ClonarDC.Services;

public sealed class BackupService
{
    public string BackupDirectory { get; }

    public BackupService()
    {
        BackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clonar DC", "Backups");
        Directory.CreateDirectory(BackupDirectory);
    }

    public async Task<string> SaveAsync(GuildSnapshot snapshot, string? displayName = null, string? description = null, IEnumerable<string>? tags = null, CancellationToken ct = default)
    {
        var payloadJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
        var envelope = new BackupEnvelope
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? $"{snapshot.Name} — {DateTime.Now:yyyy-MM-dd HH-mm}" : displayName.Trim(),
            Description = description,
            Tags = tags?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [],
            PayloadSha256 = payloadHash,
            Snapshot = snapshot
        };

        var safe = string.Concat(envelope.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(BackupDirectory, $"{safe}-{envelope.Id[..8]}.cdbak");
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            var manifest = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
                await writer.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions).AsMemory(), ct);
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public async Task<BackupEnvelope> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup sem manifest.json.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct);
        var env = JsonSerializer.Deserialize<BackupEnvelope>(json, JsonOptions) ?? throw new InvalidDataException("Backup inválido.");
        var payload = JsonSerializer.Serialize(env.Snapshot, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(env.PayloadSha256)))
            throw new InvalidDataException("Falha de integridade: o backup foi alterado ou corrompido.");
        return env;
    }

    public IReadOnlyList<string> ListBackups() => Directory.Exists(BackupDirectory)
        ? Directory.EnumerateFiles(BackupDirectory, "*.cdbak", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).ToList()
        : [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
