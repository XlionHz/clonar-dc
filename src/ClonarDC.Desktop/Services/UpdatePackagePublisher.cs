using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClonarDC.Services;

public sealed record UpdatePackageInfo(
    string PackagePath,
    Version Version,
    string Tag,
    string Title,
    string Notes,
    string SetupEntryName,
    string Sha256,
    long SetupSize);

public sealed class UpdatePackagePublisher
{
    private const string Repository = "XlionHz/clonar-dc";
    private const string ApiRoot = "https://api.github.com/repos/" + Repository;
    private readonly HttpClient _http;

    public UpdatePackagePublisher()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClonarDC-AdminPublisher", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<UpdatePackageInfo> InspectAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("The selected update package was not found.", packagePath);

        await using var file = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);

        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
            throw new InvalidOperationException("The package does not contain manifest.json.");

        UpdatePackageManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<UpdatePackageManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The update manifest is invalid.");
        }

        if (manifest.FormatVersion != 1)
            throw new InvalidOperationException("This update package format is not supported.");
        if (!Version.TryParse(manifest.Version, out var version) || version.Major < 0)
            throw new InvalidOperationException("The package version is invalid.");

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        if (version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            throw new InvalidOperationException($"The package version ({version}) must be newer than the installed app ({current.Major}.{current.Minor}.{current.Build}).");

        var setupName = string.IsNullOrWhiteSpace(manifest.SetupFile) ? "Clonar-DC-Setup.exe" : Path.GetFileName(manifest.SetupFile);
        var setupEntry = archive.Entries.FirstOrDefault(entry =>
            Path.GetFileName(entry.FullName).Equals(setupName, StringComparison.OrdinalIgnoreCase));
        if (setupEntry is null)
            throw new InvalidOperationException($"The package does not contain {setupName}.");

        var expectedHash = NormalizeHash(manifest.Sha256);
        if (expectedHash.Length != 64)
            throw new InvalidOperationException("The update package does not contain a valid SHA-256 value.");

        string actualHash;
        await using (var setupStream = setupEntry.Open())
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(setupStream, cancellationToken));
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The installer inside the package failed SHA-256 verification.");

        var tag = string.IsNullOrWhiteSpace(manifest.Tag) ? $"v{version}" : manifest.Tag.Trim();
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(?:[-.][A-Za-z0-9.-]+)?$"))
            throw new InvalidOperationException("The release tag in the package is invalid.");

        var title = string.IsNullOrWhiteSpace(manifest.Title) ? $"Clonar DC {version}" : manifest.Title.Trim();
        var notes = string.IsNullOrWhiteSpace(manifest.Notes) ? "Clonar DC update." : manifest.Notes.Trim();

        return new UpdatePackageInfo(packagePath, version, tag, title, notes, setupEntry.FullName, actualHash, setupEntry.Length);
    }

    public async Task<string> PublishAsync(
        UpdatePackageInfo package,
        string githubToken,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        githubToken = githubToken.Trim();
        if (githubToken.Length < 20)
            throw new InvalidOperationException("Enter a valid GitHub publishing key.");

        using var client = CreateAuthorizedClient(githubToken);
        progress?.Report("Checking the package version against the latest published release…");
        await EnsurePackageIsNewerThanLatestAsync(client, package.Version, cancellationToken);

        long? releaseId = null;
        try
        {
            progress?.Report("Creating a private draft release…");
            using var createResponse = await client.PostAsJsonAsync(
                $"{ApiRoot}/releases",
                new
                {
                    tag_name = package.Tag,
                    target_commitish = "main",
                    name = package.Title,
                    body = package.Notes,
                    draft = true,
                    prerelease = false,
                    generate_release_notes = false
                },
                JsonOptions,
                cancellationToken);

            if (!createResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadGitHubErrorAsync(createResponse, cancellationToken));

            var releaseNode = JsonNode.Parse(await createResponse.Content.ReadAsStringAsync(cancellationToken))
                ?? throw new InvalidOperationException("GitHub returned an invalid release response.");
            releaseId = releaseNode["id"]?.GetValue<long>()
                ?? throw new InvalidOperationException("GitHub did not return the release identifier.");
            var uploadUrl = releaseNode["upload_url"]?.GetValue<string>()?.Split('{')[0]
                ?? throw new InvalidOperationException("GitHub did not return the asset upload address.");

            progress?.Report("Uploading the installer… This may take a few minutes.");
            await UploadInstallerAsync(client, uploadUrl, package, cancellationToken);

            progress?.Report("Uploading the SHA-256 verification file…");
            var shaText = package.Sha256 + "  Clonar-DC-Setup.exe\n";
            using (var shaContent = new StringContent(shaText, Encoding.UTF8, "text/plain"))
            {
                await UploadAssetAsync(client, uploadUrl, "Clonar-DC-Setup.sha256", shaContent, cancellationToken);
            }

            progress?.Report("Publishing the release to all installations…");
            using var publishRequest = new HttpRequestMessage(HttpMethod.Patch, $"{ApiRoot}/releases/{releaseId}")
            {
                Content = JsonContent.Create(new { draft = false }, options: JsonOptions)
            };
            using var publishResponse = await client.SendAsync(publishRequest, cancellationToken);
            if (!publishResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadGitHubErrorAsync(publishResponse, cancellationToken));

            var published = JsonNode.Parse(await publishResponse.Content.ReadAsStringAsync(cancellationToken));
            return published?["html_url"]?.GetValue<string>()
                   ?? $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(package.Tag)}";
        }
        catch
        {
            if (releaseId is not null)
            {
                try { await client.DeleteAsync($"{ApiRoot}/releases/{releaseId}", CancellationToken.None); }
                catch { /* Best-effort cleanup of an incomplete draft. */ }
            }
            throw;
        }
    }

    private static HttpClient CreateAuthorizedClient(string token)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClonarDC-AdminPublisher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task EnsurePackageIsNewerThanLatestAsync(HttpClient client, Version packageVersion, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ApiRoot}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadGitHubErrorAsync(response, cancellationToken));

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = node?["tag_name"]?.GetValue<string>() ?? string.Empty;
        var match = Regex.Match(tag, @"(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)");
        if (!match.Success) return;

        var latest = new Version(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value));
        if (packageVersion <= latest)
            throw new InvalidOperationException($"Version {packageVersion} is not newer than the latest published version ({latest}).");
    }

    private static async Task UploadInstallerAsync(HttpClient client, string uploadUrl, UpdatePackageInfo package, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(package.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(package.SetupEntryName)
                    ?? throw new InvalidOperationException("The installer disappeared from the update package.");
        await using var stream = entry.Open();
        using var content = new StreamContent(stream, 81920);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = entry.Length;
        await UploadAssetAsync(client, uploadUrl, "Clonar-DC-Setup.exe", content, cancellationToken);
    }

    private static async Task UploadAssetAsync(HttpClient client, string uploadUrl, string assetName, HttpContent content, CancellationToken cancellationToken)
    {
        var url = uploadUrl + "?name=" + Uri.EscapeDataString(assetName);
        using var response = await client.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadGitHubErrorAsync(response, cancellationToken));
    }

    private static async Task<string> ReadGitHubErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var node = JsonNode.Parse(raw);
            var message = node?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return "The GitHub publishing key is invalid or expired.";
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return "The GitHub publishing key does not have Contents: Read and write permission for XlionHz/clonar-dc.";
                if ((int)response.StatusCode == 422)
                    return "This release tag already exists or GitHub rejected the release data. " + message;
                return $"GitHub error {(int)response.StatusCode}: {message}";
            }
        }
        catch { }
        return string.IsNullOrWhiteSpace(raw)
            ? $"GitHub returned HTTP {(int)response.StatusCode}."
            : $"GitHub returned HTTP {(int)response.StatusCode}: {raw}";
    }

    private static string NormalizeHash(string? value) =>
        Regex.Match(value ?? string.Empty, "[A-Fa-f0-9]{64}").Value.ToUpperInvariant();

    private sealed class UpdatePackageManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string Version { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public string? SetupFile { get; set; }
        public string? Sha256 { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}