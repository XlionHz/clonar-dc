using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private bool _discordPreviewMode;
    private string _discordPreviewReason = string.Empty;

    private static readonly List<GuildSummary> PreviewGuilds =
    [
        new("100000000000000001", "GuildSync Preview — Source", null),
        new("100000000000000002", "GuildSync Preview — Destination", null),
        new("100000000000000003", "GuildSync Preview — Community", null)
    ];

    private void AcceptTokenWithoutValidation_Click(object sender, RoutedEventArgs e)
    {
        var rawValue = TokenBox.Password ?? string.Empty;

        // Store exactly what the user entered. Discord remains the authority for real API access.
        if (RememberTokenCheck.IsChecked == true)
            _secureToken.Save(rawValue);

        _discord.SetToken(rawValue);
        _discordPreviewMode = false;
        _discordPreviewReason = string.Empty;
        _currentPlan = null;
        _currentSourceSnapshot = null;
        CloneButton.IsEnabled = false;
        ResumeCloneButton.IsEnabled = false;

        AddLog("success", LocalizeTokenSaved());
        PlanText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "Token salvo. Clique em Carregar servidores. Se o Discord não aceitar a conexão, o GuildSync abrirá uma prévia segura sem enviar alterações."
            : "Token saved. Select Load servers. If Discord does not accept the connection, GuildSync will open a safe preview without sending changes.";
    }

    private async void LoadServers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = LocalizationService.CurrentCode == "pt-BR" ? "Carregando…" : "Loading…";
        _currentPlan = null;
        _currentSourceSnapshot = null;
        CloneButton.IsEnabled = false;
        ResumeCloneButton.IsEnabled = false;

        try
        {
            var rawValue = TokenBox.Password ?? string.Empty;
            _discord.SetToken(rawValue);
            if (RememberTokenCheck.IsChecked == true)
                _secureToken.Save(rawValue);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            AddLog("info", LocalizationService.CurrentCode == "pt-BR"
                ? "Consultando os servidores acessíveis…"
                : "Loading accessible servers…");

            var guilds = await _discord.GetGuildsAsync(timeout.Token);
            if (guilds.Count == 0)
            {
                ActivatePreviewServers(LocalizationService.CurrentCode == "pt-BR"
                    ? "O Discord não retornou servidores acessíveis para este Token."
                    : "Discord returned no accessible servers for this Token.");
                return;
            }

            _discordPreviewMode = false;
            _discordPreviewReason = string.Empty;
            ApplyServerItems(guilds);

            var success = LocalizationService.CurrentCode == "pt-BR"
                ? $"{guilds.Count} servidor(es) real(is) carregado(s). Você também pode digitar qualquer ID manualmente."
                : $"Loaded {guilds.Count} real server(s). You can also type any server ID manually.";
            AddLog("success", success);
            PlanText.Text = LocalizationService.CurrentCode == "pt-BR"
                ? "Servidores carregados. Escolha origem e destino e clique em Analisar."
                : "Servers loaded. Select source and destination, then choose Analyze.";
        }
        catch (OperationCanceledException)
        {
            ActivatePreviewServers(LocalizationService.CurrentCode == "pt-BR"
                ? "A consulta ao Discord excedeu 25 segundos."
                : "The Discord request exceeded 25 seconds.");
        }
        catch (Exception ex)
        {
            ActivatePreviewServers(ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private void ActivatePreviewServers(string reason)
    {
        _discordPreviewMode = true;
        _discordPreviewReason = reason;
        ApplyServerItems(PreviewGuilds);

        var message = LocalizationService.CurrentCode == "pt-BR"
            ? "Modo de pré-visualização ativado. O Token foi mantido e a interface continuará funcionando, mas nenhuma alteração será enviada ao Discord até uma conexão real ser aceita. Motivo: " + reason
            : "Preview mode enabled. The Token was kept and the interface will continue working, but no changes will be sent to Discord until a real connection is accepted. Reason: " + reason;

        AddLog("warning", message);
        PlanText.Text = message + Environment.NewLine + Environment.NewLine +
                        (LocalizationService.CurrentCode == "pt-BR"
                            ? "Escolha os servidores de demonstração e clique em Analisar para simular todo o fluxo com segurança."
                            : "Select the demonstration servers and choose Analyze to simulate the complete flow safely.");
    }

    private void ApplyServerItems(IEnumerable<GuildSummary> guilds)
    {
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
    }

    private static string LocalizeTokenSaved() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Token aceito e salvo sem caixa de diálogo do Windows."
            : "Token accepted and saved without a Windows dialog.";
}
