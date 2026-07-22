using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private async void TestTokenReliable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (string.IsNullOrWhiteSpace(TokenBox.Password))
        {
            MessageBox.Show(
                LocalizationService.CurrentCode == "pt-BR" ? "Informe o Token do bot." : "Enter the bot Token.",
                "Clonar DC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = LocalizationService.CurrentCode == "pt-BR" ? "Testando…" : "Testing…";
        SourceGuildBox.ItemsSource = null;
        TargetGuildBox.ItemsSource = null;
        CloneButton.IsEnabled = false;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var progress = new Progress<string>(message => AddLog("info", LocalizeTokenProgress(message)));
            var result = await DiscordConnectionProbe.ValidateAndDiscoverAsync(TokenBox.Password, progress, timeout.Token);

            TokenBox.Password = result.NormalizedToken;
            _discord.SetToken(result.NormalizedToken);
            if (RememberTokenCheck.IsChecked == true) _secureToken.Save(result.NormalizedToken);

            var guilds = result.Guilds.ToList();
            SourceGuildBox.ItemsSource = guilds;
            TargetGuildBox.ItemsSource = guilds.ToList();
            if (guilds.Count > 0) SourceGuildBox.SelectedIndex = 0;
            if (guilds.Count > 1) TargetGuildBox.SelectedIndex = 1;

            AddLog("success", LocalizeTokenSuccess(result.BotName, guilds.Count));

            if (guilds.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.CurrentCode == "pt-BR"
                        ? $"Token válido. Bot identificado: {result.BotName}.\n\nEsse bot ainda não está instalado em nenhum servidor. Adicione-o aos servidores de origem e destino e teste novamente."
                        : $"Token is valid. Bot: {result.BotName}.\n\nThis bot is not installed in any server yet. Add it to both the source and destination servers, then test again.",
                    "Clonar DC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    LocalizationService.CurrentCode == "pt-BR"
                        ? $"Token válido!\n\nBot: {result.BotName}\nServidores encontrados: {guilds.Count}"
                        : $"Token is valid!\n\nBot: {result.BotName}\nServers found: {guilds.Count}",
                    "Clonar DC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "O teste demorou mais que o esperado. Verifique a conexão e tente novamente."
                : "The test took longer than expected. Check the connection and try again.";
            AddLog("error", message);
            MessageBox.Show(message, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AddLog("error", ex.Message);
            MessageBox.Show(ex.Message, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private static string LocalizeTokenProgress(string message)
    {
        if (LocalizationService.CurrentCode != "pt-BR") return message;
        return message switch
        {
            "Checking the bot identity with Discord…" => "Confirmando a identidade do bot no Discord…",
            "Token accepted. Loading the servers through the Discord Gateway…" => "Token aceito. Carregando os servidores pelo Gateway oficial do Discord…",
            _ => message
        };
    }

    private static string LocalizeTokenSuccess(string botName, int guildCount) =>
        LocalizationService.CurrentCode == "pt-BR"
            ? $"Token válido. Bot: {botName}. {guildCount} servidor(es) encontrado(s)."
            : $"Token is valid. Bot: {botName}. {guildCount} server(s) found.";
}