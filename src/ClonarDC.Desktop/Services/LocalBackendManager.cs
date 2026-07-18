using System.Diagnostics;
using System.Net;

namespace ClonarDC.Services;

internal static class LocalBackendManager
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? _process;

    public static async Task EnsureStartedAsync(
        string baseUrl,
        string? bootstrapAdminEmail = null,
        string? bootstrapAdminPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalAddress(baseUrl) || await IsReadyAsync(baseUrl, cancellationToken)) return;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (await IsReadyAsync(baseUrl, cancellationToken)) return;

            var backendExe = Path.Combine(AppContext.BaseDirectory, "backend", "ClonarDC.Server.exe");
            if (!File.Exists(backendExe))
                throw new InvalidOperationException("O serviço de contas não foi encontrado na instalação. Reinstale o Clonar DC.");

            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clonar DC",
                "backend-data");
            Directory.CreateDirectory(dataDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = backendExe,
                WorkingDirectory = Path.GetDirectoryName(backendExe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.Environment["CLONARDC_LISTEN"] = baseUrl;
            startInfo.Environment["CLONARDC_DATA"] = dataDirectory;

            if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail) && !string.IsNullOrWhiteSpace(bootstrapAdminPassword))
            {
                startInfo.Environment["CLONARDC_ADMIN_EMAIL"] = bootstrapAdminEmail;
                startInfo.Environment["CLONARDC_ADMIN_PASSWORD"] = bootstrapAdminPassword;
            }

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Não foi possível iniciar o serviço de contas.");

            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException($"O serviço de contas encerrou com o código {_process.ExitCode}.");
                if (await IsReadyAsync(baseUrl, cancellationToken)) return;
                await Task.Delay(250, cancellationToken);
            }

            throw new TimeoutException("O serviço de contas demorou demais para iniciar.");
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // O encerramento do aplicativo não deve falhar por causa do processo auxiliar.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private static bool IsLocalAddress(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsReadyAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
            using var response = await client.GetAsync(baseUrl.TrimEnd('/') + "/status", cancellationToken);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }
}