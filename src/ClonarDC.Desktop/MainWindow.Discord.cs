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

        // Replace the XAML handlers with compatibility-aware wrappers. Real tokens still
        // execute the production engine; rejected/unavailable tokens stay in a safe simulation.
        AnalyzeEngineButton.Click -= Analyze08_Click;
        AnalyzeEngineButton.Click += AnalyzeCompatible_Click;
        CloneButton.Click -= Clone08_Click;
        CloneButton.Click += CloneCompatible_Click;

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
            if (source.Id == target.Id)
                throw new InvalidOperationException("The source and destination servers cannot be the same.");

            var mode = SelectedMode();
            _currentSourceSnapshot = CreatePreviewSnapshot(source);
            _currentPlan = new ClonePlan
            {
                SourceGuildId = source.Id,
                SourceGuildName = source.Name,
                TargetGuildId = target.Id,
                TargetGuildName = target.Name,
                Mode = mode,
                RolesToCreate = 4,
                RolesToUpdate = mode == "merge" ? 2 : 0,
                RolesToReuse = 1,
                ChannelsToCreate = 6,
                ChannelsToUpdate = mode == "merge" ? 2 : 0,
                ChannelsToReuse = 2,
                EmojisToCreate = 3,
                EmojisToReuse = 1,
                TargetRolesToDelete = mode == "exact" ? 3 : 0,
                TargetChannelsToDelete = mode == "exact" ? 5 : 0,
                TargetEmojisToDelete = mode == "exact" ? 2 : 0,
                RiskScore = mode == "exact" ? 72 : mode == "merge" ? 34 : 12,
                Warnings =
                [
                    "PREVIEW ONLY — no Discord request will be sent.",
                    "Connect a real accepted Token and reload servers before executing changes."
                ]
            };

            PlanText.Text = BuildPlanText08(_currentPlan) + Environment.NewLine + Environment.NewLine +
                            "Preview reason: " + _discordPreviewReason;
            CloneButton.IsEnabled = true;
            ResumeCloneButton.IsEnabled = false;
            AddLog("success", "Preview analysis generated. No Discord data was read or changed.");
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
            AddLog("warning", "Run the preview analysis before starting the simulation.");
            return;
        }

        CloneButton.IsEnabled = false;
        AnalyzeEngineButton.IsEnabled = false;
        try
        {
            var steps = new[]
            {
                "Checking source and destination selection…",
                "Simulating roles and permission mapping…",
                "Simulating categories and channels…",
                "Simulating emoji transfer…",
                "Simulating post-operation verification…"
            };

            foreach (var step in steps)
            {
                AddLog("info", step);
                await Task.Delay(180);
            }

            AddLog("success", "Preview simulation completed. Zero requests were sent to Discord.");
            AddOperation($"Preview completed: {_currentPlan.SourceGuildName} → {_currentPlan.TargetGuildName}");
            DashboardLastOperation.Text = $"Preview → {_currentPlan.TargetGuildName}";
            PlanText.Text += Environment.NewLine + Environment.NewLine +
                             "SIMULATION COMPLETED — the interface and workflow ran successfully; no Discord content was changed.";
        }
        finally
        {
            AnalyzeEngineButton.IsEnabled = true;
            CloneButton.IsEnabled = true;
        }
    }

    private static GuildSnapshot CreatePreviewSnapshot(GuildSummary source) => new()
    {
        SourceGuildId = source.Id,
        Name = source.Name,
        Roles =
        [
            new RoleSnapshot { Id = source.Id, Name = "@everyone", Position = 0 },
            new RoleSnapshot { Id = "preview-role-admin", Name = "Admin", Position = 3 },
            new RoleSnapshot { Id = "preview-role-member", Name = "Member", Position = 2 },
            new RoleSnapshot { Id = "preview-role-bot", Name = "GuildSync", Position = 1, Managed = true }
        ],
        Channels =
        [
            new ChannelSnapshot { Id = "preview-category", Name = "COMMUNITY", Type = 4, Position = 0 },
            new ChannelSnapshot { Id = "preview-general", Name = "general", Type = 0, ParentId = "preview-category", Position = 1 },
            new ChannelSnapshot { Id = "preview-news", Name = "announcements", Type = 5, ParentId = "preview-category", Position = 2 },
            new ChannelSnapshot { Id = "preview-voice", Name = "Voice", Type = 2, ParentId = "preview-category", Position = 3 }
        ],
        Emojis =
        [
            new EmojiSnapshot { Id = "preview-emoji-1", Name = "guildsync" },
            new EmojiSnapshot { Id = "preview-emoji-2", Name = "verified" }
        ]
    };

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
