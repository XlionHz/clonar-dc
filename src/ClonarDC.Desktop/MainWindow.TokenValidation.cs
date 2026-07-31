using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private bool _discordPreviewMode = true;
    private string _discordPreviewReason = "No official Discord connection has been established yet.";

    private void AcceptTokenWithoutValidation_Click(object sender, RoutedEventArgs e)
    {
        var rawValue = TokenBox.Password ?? string.Empty;

        // The exact value is accepted for the current session. Persistence remains an explicit
        // user choice through RememberTokenCheck; no format/type classification occurs here.
        _discordProviders.SetCredential(rawValue);
        if (RememberTokenCheck.IsChecked == true)
            _secureToken.Save(rawValue);

        _discordPreviewMode = true;
        _discordPreviewReason = "The value changed. Reload servers to establish the official provider or activate simulation.";
        _currentPlan = null;
        _currentSourceSnapshot = null;
        CloneButton.IsEnabled = false;
        ResumeCloneButton.IsEnabled = false;

        AddLog("success", LocalizeTokenSaved());
        ProviderStatusText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "Provedor: valor armazenado; aguardando Carregar servidores."
            : "Provider: value stored; waiting for Load servers.";
        PlanText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "Valor salvo exatamente como digitado. Clique em Carregar servidores. O GuildSync tentará o provedor oficial e, se não houver conexão autorizada, ativará o provedor simulado completo."
            : "Value saved exactly as entered. Select Load servers. GuildSync will try the official provider and use the complete simulated provider when no authorized connection is available.";
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
            _discordProviders.SetCredential(rawValue);
            if (RememberTokenCheck.IsChecked == true)
                _secureToken.Save(rawValue);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            AddLog("info", LocalizationService.CurrentCode == "pt-BR"
                ? "Consultando o provedor oficial disponível…"
                : "Checking the available official provider…");

            var result = await _discordProviders.LoadGuildsAsync(timeout.Token);
            _discordPreviewMode = result.IsSimulated;
            _discordPreviewReason = result.FallbackReason ?? string.Empty;
            ApplyServerItems(result.Guilds);

            if (result.IsSimulated)
            {
                ActivatePreviewServers(result.FallbackReason ?? "The official provider was unavailable.");
                return;
            }

            var success = LocalizationService.CurrentCode == "pt-BR"
                ? $"{result.Guilds.Count} servidor(es) carregado(s) pelo provedor oficial. Você também pode digitar qualquer ID manualmente."
                : $"Loaded {result.Guilds.Count} server(s) through the official provider. You can also type any server ID manually.";
            AddLog("success", success);
            ProviderStatusText.Text = LocalizationService.CurrentCode == "pt-BR"
                ? $"Provedor oficial ativo • {result.Guilds.Count} servidor(es)"
                : $"Official provider active • {result.Guilds.Count} server(s)";
            PlanText.Text = LocalizationService.CurrentCode == "pt-BR"
                ? "Servidores carregados. Escolha origem e destino e clique em Analisar."
                : "Servers loaded. Select source and destination, then choose Analyze.";
        }
        catch (OperationCanceledException)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "A consulta ao provedor oficial excedeu 25 segundos."
                : "The official provider request exceeded 25 seconds.";
            if (_discordProviders.RestrictivePolicyEnabled)
            {
                _discordPreviewMode = false;
                _discordPreviewReason = string.Empty;
                ProviderStatusText.Text = message;
                PlanText.Text = message;
                AddLog("error", message);
            }
            else
            {
                ActivatePreviewServers(message);
            }
        }
        catch (Exception ex)
        {
            if (_discordProviders.RestrictivePolicyEnabled)
            {
                _discordPreviewMode = false;
                _discordPreviewReason = string.Empty;
                PlanText.Text = ex.Message;
                AddLog("error", ex.Message);
            }
            else
            {
                ActivatePreviewServers(ex.Message);
            }
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
        ApplyServerItems(_discordProviders.GetSimulatedGuilds());

        var message = LocalizationService.CurrentCode == "pt-BR"
            ? "PROVEDOR SIMULADO ATIVO — o valor digitado foi mantido exatamente e toda a interface continuará funcional. Nenhuma solicitação de alteração será enviada ao Discord. Motivo: " + reason
            : "SIMULATED PROVIDER ACTIVE — the entered value was preserved exactly and the complete interface remains functional. No change request will be sent to Discord. Reason: " + reason;

        AddLog("warning", message);
        ProviderStatusText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "Provedor simulado ativo • fluxo completo sem alterações reais"
            : "Simulated provider active • complete flow without real changes";
        PlanText.Text = message + Environment.NewLine + Environment.NewLine +
                        (LocalizationService.CurrentCode == "pt-BR"
                            ? "Escolha os servidores simulados e clique em Analisar para testar o fluxo completo."
                            : "Select the simulated servers and choose Analyze to test the complete workflow.");
    }

    private void ApplyServerItems(IEnumerable<GuildSummary> guilds)
    {
        var sourceItems = guilds.ToList();
        var targetItems = guilds.ToList();
        var previousSource = SourceGuildBox.SelectedItem as GuildSummary;
        var previousTarget = TargetGuildBox.SelectedItem as GuildSummary;
        var previousSourceText = SourceGuildBox.Text;
        var previousTargetText = TargetGuildBox.Text;

        SourceGuildBox.ItemsSource = sourceItems;
        TargetGuildBox.ItemsSource = targetItems;

        RestoreServerSelection(SourceGuildBox, sourceItems, previousSource, previousSourceText, 0);
        RestoreServerSelection(TargetGuildBox, targetItems, previousTarget, previousTargetText, sourceItems.Count > 1 ? 1 : 0);
    }

    private static void RestoreServerSelection(
        ComboBox box,
        IReadOnlyList<GuildSummary> items,
        GuildSummary? previous,
        string? previousText,
        int defaultIndex)
    {
        var retained = previous is null
            ? null
            : items.FirstOrDefault(item => string.Equals(item.Id, previous.Id, StringComparison.Ordinal));

        if (retained is not null)
        {
            box.SelectedItem = retained;
            box.Text = retained.Name;
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousText))
        {
            var typedMatch = items.FirstOrDefault(item =>
                string.Equals(item.Id, previousText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, previousText, StringComparison.CurrentCultureIgnoreCase));
            if (typedMatch is not null)
            {
                box.SelectedItem = typedMatch;
                box.Text = typedMatch.Name;
            }
            else
            {
                box.SelectedItem = null;
                box.Text = previousText;
            }
            return;
        }

        if (items.Count == 0) return;
        var index = Math.Clamp(defaultIndex, 0, items.Count - 1);
        box.SelectedIndex = index;
        box.Text = items[index].Name;
    }

    private static string LocalizeTokenSaved() =>
        LocalizationService.CurrentCode == "pt-BR"
            ? "Valor aceito e salvo sem validação restritiva, caixa externa ou som do Windows."
            : "Value accepted and saved without restrictive validation, external dialogs or Windows warning sounds.";
}
