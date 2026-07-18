using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClonarDC.Services;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string SetupUrl,
    string Sha256Url,
    string ReleasePageUrl,
    string Notes);

public sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/XlionHz/clonar-dc/releases/latest";
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClonarDC", CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("O servidor de atualizações retornou uma resposta inválida.");

        var version = ParseVersion(release.TagName)
            ?? throw new InvalidOperationException("A versão publicada não pôde ser interpretada.");

        if (version <= CurrentVersion) return null;

        var setup = release.Assets.FirstOrDefault(a =>
            a.Name.Equals("Clonar-DC-Setup.exe", StringComparison.OrdinalIgnoreCase));
        var sha = release.Assets.FirstOrDefault(a =>
            a.Name.Equals("Clonar-DC-Setup.sha256", StringComparison.OrdinalIgnoreCase));

        if (setup is null || sha is null)
            throw new InvalidOperationException("A publicação mais recente não contém o instalador e o SHA-256 necessários.");

        return new UpdateInfo(
            version,
            release.TagName,
            setup.BrowserDownloadUrl,
            sha.BrowserDownloadUrl,
            release.HtmlUrl,
            release.Body ?? string.Empty);
    }

    public async Task<string> DownloadAndVerifyAsync(
        UpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clonar DC",
            "updates",
            update.Tag.Replace('/', '-'));
        Directory.CreateDirectory(updateDirectory);

        var setupPath = Path.Combine(updateDirectory, "Clonar-DC-Setup.exe");
        var temporaryPath = setupPath + ".download";

        using (var response = await _http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (total is > 0)
                    progress?.Report((int)Math.Clamp(downloaded * 100 / total.Value, 0, 100));
            }
        }

        var shaText = await _http.GetStringAsync(update.Sha256Url, cancellationToken);
        var expectedHash = Regex.Match(shaText, "[A-Fa-f0-9]{64}").Value.ToUpperInvariant();
        if (expectedHash.Length != 64)
            throw new InvalidOperationException("O arquivo de verificação da atualização é inválido.");

        await using var file = File.OpenRead(temporaryPath);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporaryPath);
            throw new InvalidOperationException("A atualização baixada falhou na verificação de integridade.");
        }

        File.Move(temporaryPath, setupPath, true);
        progress?.Report(100);
        return setupPath;
    }

    public static void LaunchInstaller(string setupPath)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("O instalador baixado não foi encontrado.", setupPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath)!
        });
    }

    private static Version? ParseVersion(string tag)
    {
        var match = Regex.Match(tag, @"(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[A-Za-z]+[.-](?<build>\d+))?");
        if (!match.Success) return null;
        var build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value) : 0;
        return new Version(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            build);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
