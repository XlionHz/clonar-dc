using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private bool _compatibilityUiInitialized;
    private readonly TextBox DiscordLinkCodeBox = new() { Height = 40, MaxLength = 32, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock DiscordLinkStatusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button LinkDiscordButton = new() { Content = "Connect Discord account", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

    private void InitializeCompatibilityUi()
    {
        if (_compatibilityUiInitialized) return;
        _compatibilityUiInitialized = true;

        // Route the existing interface through either the official provider or the complete
        // simulated provider. Token admission remains permissive by default.
        AnalyzeEngineButton.Click -= Analyze08_Click;
        AnalyzeEngineButton.Click += AnalyzeCompatible_Click;
        CloneButton.Click -= Clone08_Click;
        CloneButton.Click += CloneCompatible_Click;
        RestoreBackupButton08.Click -= RestoreBackup08_Click;
        RestoreBackupButton08.Click += RestoreBackupCompatible_Click;

        LinkDiscordButton.Click += LinkDiscord_Click;
        DiscordLinkStatusText.Text = "Generate a one-time code with /link in the official GuildSync bot.";
        DiscordLinkStatusText.Foreground = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mark = BrandPresentation.CreateMark(38, glow: false);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(mark);

        var title = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Discord account", FontSize = 18, FontWeight = FontWeights.Bold });
        title.Children.Add(new TextBlock
        {
            Text = "Link the desktop account to the official GuildSync bot.",
            Foreground = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock { Text = "One-time link code", Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(DiscordLinkCodeBox);
        panel.Children.Add(LinkDiscordButton);
        panel.Children.Add(DiscordLinkStatusText);

        DeviceManagementHost.Children.Add(new Border
        {
            Background = TryFindResource("PanelBrush") as Brush,
            BorderBrush = TryFindResource("BorderBrush") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 18, 0, 0),
            Child = panel
        });

        LanguageBox.SelectedIndex = LocalizationService.CurrentCode.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? 0
            : LocalizationService.CurrentCode.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
    }

    private void AnalyzeCompatible_Click(object sender, RoutedEventArgs e)
    {
        if (!_discordPreviewMode)
        {
            Analyze08_Click(sender, e);
            return;
        }

        try
        {
            var source = RequireEditableGuild(SourceGuildBox, "source server");
            var target = RequireEditableGuild(TargetGuildBox, "destination server");
            var analysis = _discordProviders.CreateSimulatedAnalysis(source, target, SelectedMode());
            _currentSourceSnapshot = analysis.SourceSnapshot;
            _currentPlan = analysis.Plan;

            PlanText.Text = BuildPlanText08(_currentPlan) + Environment.NewLine + Environment.NewLine +
                            "Simulated provider reason: " + _discordPreviewReason;
            CloneButton.IsEnabled = true;
            ResumeCloneButton.IsEnabled = false;
            AddLog("success", "Simulated analysis generated with roles, channels, emojis and verification steps. No Discord request was sent.");
        }
        catch (Exception ex)
        {
            PlanText.Text = ex.Message;
            AddLog("error", ex.Message);
        }
    }

    private async void CloneCompatible_Click(object sender, RoutedEventArgs e)
    {
        if (!_discordPreviewMode)
        {
            Clone08_Click(sender, e);
            return;
        }

        if (_currentPlan is null || _currentSourceSnapshot is null)
        {
            var message = LocalizationService.CurrentCode == "pt-BR"
                ? "Execute a análise simulada antes de iniciar."
                : "Run the simulated analysis before starting.";
            AddLog("warning", message);
            PlanText.Text = message;
            return;
        }

        CloneButton.IsEnabled = false;
        AnalyzeEngineButton.IsEnabled = false;
        CancelCloneButton.IsEnabled = true;
        _operationCancellation08?.Dispose();
        _operationCancellation08 = new CancellationTokenSource();
        try
        {
            await _discordProviders.RunSimulatedOperationAsync(MakeProgress(), _operationCancellation08.Token);
            AddOperation($"Simulated operation completed: {_currentPlan.SourceGuildName} → {_currentPlan.TargetGuildName}");
            DashboardLastOperation.Text = $"Simulation → {_currentPlan.TargetGuildName}";
            PlanText.Text += Environment.NewLine + Environment.NewLine +
                             "SIMULATION COMPLETED AND VERIFIED — every provider-independent step ran successfully; no Discord content was changed.";
        }
        catch (OperationCanceledException)
        {
            AddLog("warning", "The simulated operation was cancelled.");
        }
        catch (Exception ex)
        {
            AddLog("error", ex.Message);
            PlanText.Text += Environment.NewLine + Environment.NewLine + ex.Message;
        }
        finally
        {
            _operationCancellation08?.Dispose();
            _operationCancellation08 = null;
            AnalyzeEngineButton.IsEnabled = true;
            CloneButton.IsEnabled = true;
            CancelCloneButton.IsEnabled = false;
        }
    }

    private async void RestoreBackupCompatible_Click(object sender, RoutedEventArgs e)
    {
        if (!_discordPreviewMode)
        {
            RestoreBackup08_Click(sender, e);
            return;
        }

        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count)
        {
            BackupDetails.Text = "Select a backup before running the simulated restore.";
            AddLog("warning", BackupDetails.Text);
            return;
        }

        try
        {
            var envelope = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            var target = RequireEditableGuild(TargetGuildBox, "destination server");
            RestoreBackupButton08.IsEnabled = false;
            await _discordProviders.RunSimulatedOperationAsync(MakeProgress());
            BackupDetails.Text = $"SIMULATED RESTORE VERIFIED\nBackup: {envelope.Name}\nDestination: {target.Name}\nNo Discord request was sent.";
            AddOperation($"Simulated restore completed: {envelope.Name} → {target.Name}");
            AddLog("success", "The complete restore flow was simulated and verified without changing Discord.");
        }
        catch (Exception ex)
        {
            BackupDetails.Text = "Simulated restore failed: " + ex.Message;
            AddLog("error", ex.Message);
        }
        finally
        {
            RestoreBackupButton08.IsEnabled = true;
        }
    }

    private async void LinkDiscord_Click(object sender, RoutedEventArgs e)
    {
        var code = DiscordLinkCodeBox.Text.Trim().ToUpperInvariant();
        if (code.Length < 6)
        {
            DiscordLinkStatusText.Text = "Enter the one-time code generated by /link in the official GuildSync bot.";
            return;
        }

        LinkDiscordButton.IsEnabled = false;
        DiscordLinkStatusText.Text = "Connecting your Discord account…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await _auth.LinkDiscordAsync(_session, code, timeout.Token);
            DiscordLinkCodeBox.Text = string.Empty;
            DiscordLinkStatusText.Text = result.Linked
                ? $"Discord connected successfully. Account ID: {result.DiscordUserId}."
                : "The Discord account could not be connected.";
        }
        catch (OperationCanceledException)
        {
            DiscordLinkStatusText.Text = "The request took too long. Generate a new /link code and try again.";
        }
        catch (Exception ex)
        {
            DiscordLinkStatusText.Text = ex.Message;
        }
        finally
        {
            LinkDiscordButton.IsEnabled = true;
        }
    }

    private void ForgetToken_Click(object sender, RoutedEventArgs e) => ClearToken_Click(sender, e);
    private async void RefreshAdmin_Click(object sender, RoutedEventArgs e) => await LoadAdminUsersAsync();
    private void AdminResetDevices08_Click(object sender, RoutedEventArgs e) => ResetSelectedUserDevices08_Click(sender, e);

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_compatibilityUiInitialized || LanguageBox.SelectedItem is not ComboBoxItem selected) return;
        var code = selected.Tag?.ToString();
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) code = "en-US";
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, LocalizationService.CurrentCode, StringComparison.OrdinalIgnoreCase)) return;

        LocalizationService.Save(code);
        var message = code.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? "Idioma salvo. Reabra o GuildSync para aplicar todos os textos."
            : "Language saved. Reopen GuildSync to apply every label.";
        AddLog("success", message);
        DiscordLinkStatusText.Text = message;
    }
}
