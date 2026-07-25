namespace ClonarDC.Services;

public sealed class OperationReportService
{
    private const long MaximumReportBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string OperationDirectory { get; }

    public OperationReportService()
    {
        OperationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuildSync",
            "Operations");
        Directory.CreateDirectory(OperationDirectory);
    }

    public string GetPath(string reportId) =>
        Path.Combine(OperationDirectory, SanitizeId(reportId) + ".json");

    public async Task<string> SaveAsync(CloneExecutionReport report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var path = GetPath(report.Id);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        if (bytes.LongLength > MaximumReportBytes)
            throw new InvalidDataException("O relatório da operação excedeu o limite seguro de tamanho.");

        Directory.CreateDirectory(OperationDirectory);
        await File.WriteAllBytesAsync(temporary, bytes, ct);
        File.Move(temporary, path, true);
        return path;
    }

    public async Task<CloneExecutionReport> LoadAsync(string path, CancellationToken ct = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Relatório de operação não encontrado.", path);
        if (file.Length <= 0 || file.Length > MaximumReportBytes)
            throw new InvalidDataException("Relatório de operação inválido ou demasiado grande.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var report = await JsonSerializer.DeserializeAsync<CloneExecutionReport>(stream, JsonOptions, ct)
                     ?? throw new InvalidDataException("Relatório de operação inválido.");
        if (string.IsNullOrWhiteSpace(report.Id) || string.IsNullOrWhiteSpace(report.TargetGuildId))
            throw new InvalidDataException("Relatório de operação incompleto.");
        return report;
    }

    public IReadOnlyList<string> ListReports() => Directory.Exists(OperationDirectory)
        ? Directory.EnumerateFiles(OperationDirectory, "op_*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList()
        : [];

    public async Task<CloneExecutionReport?> FindLatestResumableAsync(
        string sourceGuildId,
        string targetGuildId,
        string mode,
        CancellationToken ct = default)
    {
        foreach (var path in ListReports().Take(50))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var report = await LoadAsync(path, ct);
                if (report.CanResume &&
                    string.Equals(report.SourceGuildId, sourceGuildId, StringComparison.Ordinal) &&
                    string.Equals(report.TargetGuildId, targetGuildId, StringComparison.Ordinal) &&
                    string.Equals(report.Mode, mode, StringComparison.OrdinalIgnoreCase))
                    return report;
            }
            catch
            {
                // A damaged historical report must not block newer valid reports.
            }
        }
        return null;
    }

    public async Task<string> ExportSummaryAsync(CloneExecutionReport report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var path = Path.Combine(OperationDirectory, SanitizeId(report.Id) + "-summary.txt");
        var lines = new List<string>
        {
            $"GuildSync operation {report.Id}",
            $"Status: {report.Status}",
            $"Source: {report.SourceGuildName} ({report.SourceGuildId})",
            $"Target: {report.TargetGuildName} ({report.TargetGuildId})",
            $"Mode: {report.Mode}",
            $"Started: {report.StartedAt:O}",
            $"Completed: {report.CompletedAt:O}",
            $"Steps completed: {report.CompletedSteps}/{report.Steps.Count}",
            $"Failed steps: {report.FailedSteps}",
            string.Empty,
            "Steps:"
        };
        lines.AddRange(report.Steps.Select(step =>
            $"[{step.Status}] {step.Kind} — {step.Name}: {step.Message}"));
        if (report.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Warnings:");
            lines.AddRange(report.Warnings.Select(value => "- " + value));
        }
        if (report.Errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Errors:");
            lines.AddRange(report.Errors.Select(value => "- " + value));
        }
        if (report.Verification is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Verification: {(report.Verification.Passed ? "passed" : "differences found")}");
            lines.AddRange(report.Verification.Differences.Select(value => "- " + value));
        }

        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, ct);
        return path;
    }

    private static string SanitizeId(string value)
    {
        var cleaned = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "op_" + Guid.NewGuid().ToString("N") : cleaned[..Math.Min(cleaned.Length, 96)];
    }
}