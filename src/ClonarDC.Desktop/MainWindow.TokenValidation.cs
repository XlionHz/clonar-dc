using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private void AcceptTokenWithoutValidation_Click(object sender, RoutedEventArgs e)
    {
        var rawValue = TokenBox.Password ?? string.Empty;

        // Intentionally no format, length, prefix, bot/user or network validation here.
        // The value is only stored locally. Discord is contacted later, when the user
        // explicitly requests server loading or starts an operation.
        if (RememberTokenCheck.IsChecked == true)
            _secureToken.Save(rawValue);

        AddLog("success", LocalizeLocalTokenAccepted());
        MessageBox.Show(
            LocalizeLocalTokenAcceptedBody(),
            "Clonar DC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void LoadServers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = LocalizationService.CurrentCode == "pt-BR" ? "Carregando…" : "Loading…";
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
            if (RememberTokenCheck.IsChecked == true)
                _secureToken.Save(TokenBox.Password);

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
                        ? $"Credencial aceita pelo Discord. Bot identificado: {result.BotName}.\n\nEsse bot ainda não está instalado em nenhum servidor. Adicione-o aos servidores de origem e destino e carregue novamente."
                        : $"Discord accepted the credential. Bot: {result.BotName}.\n\nThis bot is not installed in any server yet. Add it to both the source and destination servers, then load again.",
                    "Clonar DC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "O carregamento demorou mais que o esperado. O valor continua no campo e não foi apagado."
                : "Loading took longer than expected. The value remains in the field and was not removed.";
            AddLog("warning", message);
            MessageBox.Show(message, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // Connection errors never invalidate, clear or reject the locally entered value.
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "O valor foi aceito localmente, mas o Discord não conseguiu carregar os servidores agora.\n\n" + ex.Message
                : "The value was accepted locally, but Discord could not load the servers right now.\n\n" + ex.Message;
            AddLog("warning", message);
            MessageBox.Show(message, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private static string LocalizeLocalTokenAccepted() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Valor do Token aceito localmente sem validação."
            : "Token value accepted locally without validation.";

    private static string LocalizeLocalTokenAcceptedBody() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Pronto. O aplicativo não verificou formato, tamanho ou tipo da credencial. O Discord só será consultado quando você clicar em Carregar servidores ou iniciar uma operação."
            : "Done. The app did not check the credential format, length, or type. Discord is contacted only when you load servers or start an operation.";

    private static string LocalizeTokenProgress(string message)
    {
        if (LocalizationService.CurrentCode != "pt-BR") return message;
        return message switch
        {
            "Checking the bot identity with Discord…" => "Conectando ao Discord para carregar os servidores…",
            "Token accepted. Loading the servers through the Discord Gateway…" => "Conexão aceita. Carregando os servidores pelo Gateway do Discord…",
            _ => message
        };
    }

    private static string LocalizeTokenSuccess(string botName, int guildCount) =>
        LocalizationService.CurrentCode == "pt-BR"
            ? $"Conexão concluída. Bot: {botName}. {guildCount} servidor(es) encontrado(s)."
            : $"Connection completed. Bot: {botName}. {guildCount} server(s) found.";
}