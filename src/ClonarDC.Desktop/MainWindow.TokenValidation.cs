using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private void AcceptTokenWithoutValidation_Click(object sender, RoutedEventArgs e)
    {
        var rawValue = TokenBox.Password ?? string.Empty;

        // The desktop intentionally does not classify, normalize or reject the value here.
        // It is stored exactly as entered. Any later service response is shown as a
        // connection result, never as a local "token type" decision.
        if (RememberTokenCheck.IsChecked == true)
            _secureToken.Save(rawValue);

        _discord.SetToken(rawValue);
        AddLog("success", LocalizeTokenSaved());
        MessageBox.Show(
            LocalizeTokenSavedBody(),
            "GuildSync",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void LoadServers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = LocalizationService.CurrentCode == "pt-BR" ? "Carregando…" : "Loading…";
        CloneButton.IsEnabled = false;

        try
        {
            var rawValue = TokenBox.Password ?? string.Empty;
            _discord.SetToken(rawValue);
            if (RememberTokenCheck.IsChecked == true)
                _secureToken.Save(rawValue);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            AddLog("info", LocalizationService.CurrentCode == "pt-BR"
                ? "Consultando os servidores acessíveis…"
                : "Loading accessible servers…");

            // There is no local format, size, prefix, bot/user or identity validation.
            // The connected service is the only authority that can accept or reject a request.
            var guilds = await _discord.GetGuildsAsync(timeout.Token);
            var sourceItems = guilds.ToList();
            var targetItems = guilds.ToList();
            SourceGuildBox.ItemsSource = sourceItems;
            TargetGuildBox.ItemsSource = targetItems;

            if (sourceItems.Count > 0)
            {
                SourceGuildBox.SelectedIndex = 0;
                SourceGuildBox.Text = sourceItems[0].Name;
            }
            if (targetItems.Count > 1)
            {
                TargetGuildBox.SelectedIndex = 1;
                TargetGuildBox.Text = targetItems[1].Name;
            }
            else if (targetItems.Count == 1)
            {
                TargetGuildBox.SelectedIndex = 0;
                TargetGuildBox.Text = targetItems[0].Name;
            }

            var success = LocalizationService.CurrentCode == "pt-BR"
                ? $"{guilds.Count} servidor(es) carregado(s). Você também pode digitar qualquer ID manualmente."
                : $"Loaded {guilds.Count} server(s). You can also type any server ID manually.";
            AddLog("success", success);
        }
        catch (OperationCanceledException)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "O carregamento demorou mais que o esperado. O Token e os IDs digitados foram mantidos."
                : "Loading took longer than expected. The Token and typed IDs were preserved.";
            AddLog("warning", message);
            MessageBox.Show(message, "GuildSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "O Token foi aceito pelo aplicativo, mas o serviço não conseguiu carregar os servidores agora. Você ainda pode digitar os IDs manualmente.\n\n" + ex.Message
                : "The app accepted the Token, but the service could not load servers now. You can still type the IDs manually.\n\n" + ex.Message;
            AddLog("warning", message);
            MessageBox.Show(message, "GuildSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private static string LocalizeTokenSaved() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Token aceito sem validação local."
            : "Token accepted without local validation.";

    private static string LocalizeTokenSavedBody() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Pronto. O valor foi aceito exatamente como inserido. O aplicativo não verificou formato, tamanho, prefixo ou tipo."
            : "Done. The value was accepted exactly as entered. The app did not check its format, length, prefix, or type.";
}
