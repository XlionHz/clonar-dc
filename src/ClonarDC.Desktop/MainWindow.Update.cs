using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private bool _automaticUpdateCheckStarted;
    private CancellationTokenSource? _updateCts;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_automaticUpdateCheckStarted) return;
        _automaticUpdateCheckStarted = true;
        await Task.Delay(2500);
        await CheckForUpdatesAsync(showCurrentMessage: false);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showCurrentMessage: true);
    }

    private async Task CheckForUpdatesAsync(bool showCurrentMessage)
    {
        if (_updateCts is not null) return;

        _updateCts = new CancellationTokenSource();
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Verificando atualizações…";

        try
        {
            var update = await _updateService.CheckAsync(_updateCts.Token);
            if (update is null)
            {
                UpdateStatusText.Text = $"Versão atual: {_updateService.CurrentVersion}. Nenhuma atualização disponível.";
                if (showCurrentMessage)
                    MessageBox.Show("Você já está usando a versão mais recente.", "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateStatusText.Text = $"Nova versão disponível: {update.Tag}.";
            var answer = MessageBox.Show(
                $"A versão {update.Tag} está disponível.\n\nO Clonar DC pode baixar, verificar e instalar a atualização automaticamente. O aplicativo será fechado durante a instalação.\n\nAtualizar agora?",
                "Atualização disponível",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes) return;

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText.Text = $"Baixando atualização… {percent}%";
            });

            var setupPath = await _updateService.DownloadAndVerifyAsync(update, progress, _updateCts.Token);
            UpdateStatusText.Text = "Download concluído e integridade verificada. Instalando…";
            UpdateService.LaunchInstaller(setupPath);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "Verificação de atualização cancelada.";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Não foi possível atualizar: " + ex.Message;
            if (showCurrentMessage)
                MessageBox.Show(UpdateStatusText.Text, "Atualizações", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            _updateCts.Dispose();
            _updateCts = null;
        }
    }
}
