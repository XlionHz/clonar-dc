using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private List<DeviceDto> _devices08 = [];
    private bool _deviceUiInitialized08;
    private readonly DiscordPreflightService _preflight08 = new();
    private readonly TextBlock DeviceLimitText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private readonly ListBox DevicesList = new() { MinHeight = 120, MaxHeight = 230 };
    private readonly Button RevokeDeviceButton08 = new() { Content = "Remove selected device", IsEnabled = false, Margin = new Thickness(8, 10, 0, 0) };

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_deviceUiInitialized08) return;
        _deviceUiInitialized08 = true;
        BuildDeviceUi08();
        AnalyzeEngineButton.Click -= Analyze08_Click;
        AnalyzeEngineButton.Click += AnalyzeWithPreflight08_Click;
        Closed += (_, _) => _preflight08.Dispose();
        _ = InitializeDevices08Async();
    }

    private void BuildDeviceUi08()
    {
        DevicesList.SelectionChanged += DevicesList_SelectionChanged08;
        RevokeDeviceButton08.Click += RevokeDevice08_Click;
        var refresh = new Button { Content = "Refresh devices", Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += RefreshDevices08_Click;

        var devicePanel = new StackPanel();
        devicePanel.Children.Add(new TextBlock
        {
            Text = "Licensed devices",
            FontSize = 18,
            FontWeight = FontWeights.Bold
        });
        devicePanel.Children.Add(new TextBlock
        {
            Text = "GuildSync stores only a cryptographic hash of the protected device identity. Removing a device immediately ends sessions from that computer.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("MutedBrush") as Brush,
            Margin = new Thickness(0, 8, 0, 0)
        });
        devicePanel.Children.Add(DeviceLimitText);
        devicePanel.Children.Add(DevicesList);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(refresh);
        buttons.Children.Add(RevokeDeviceButton08);
        devicePanel.Children.Add(buttons);

        var border = new Border
        {
            Background = TryFindResource("PanelBrush") as Brush,
            BorderBrush = TryFindResource("BorderBrush") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 18, 0, 0),
            Child = devicePanel
        };

        if (Pages.Items.Count > 5 && Pages.Items[5] is TabItem settingsTab &&
            settingsTab.Content is ScrollViewer scroll && scroll.Content is StackPanel settingsRoot)
            settingsRoot.Children.Add(border);

        if (_session.IsAdmin && LicenseBox.Parent is StackPanel adminActions)
        {
            var reset = new Button
            {
                Content = "Reset licensed devices",
                Margin = new Thickness(0, 8, 0, 0)
            };
            reset.Click += ResetSelectedUserDevices08_Click;
            adminActions.Children.Add(reset);
        }
    }

    private async void AnalyzeWithPreflight08_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var engine = RequireEngine08();
            var source = RequireGuild(SourceGuildBox, "source server");
            var target = RequireGuild(TargetGuildBox, "destination server");
            var mode = SelectedMode();
            CloneButton.IsEnabled = false;
            ResumeCloneButton.IsEnabled = false;
            PlanText.Text = "Analyzing structure, permissions and role hierarchy…";
            AddLog("info", $"Analyzing {source.Name} → {target.Name} in {mode} mode…");

            var result = await engine.AnalyzeAsync(source.Id, target.Id, mode, MakeProgress());
            _preflight08.SetToken(TokenBox.Password);
            var preflight = await _preflight08.CheckAsync(
                target.Id,
                mode,
                result.Source.Emojis.Count > 0);
            result.Plan.BlockingIssues.AddRange(preflight.BlockingIssues);
            result.Plan.Warnings.AddRange(preflight.Warnings);
            result.Plan.RiskScore = Math.Clamp(
                result.Plan.RiskScore + result.Plan.BlockingIssues.Count * 25 + preflight.Warnings.Count * 3,
                0,
                100);

            _currentPlan = result.Plan;
            _currentSourceSnapshot = result.Source;
            PlanText.Text = BuildPlanWithPreflight08(result.Plan, preflight);
            CloneButton.IsEnabled = result.Plan.CanExecute;
            var resumable = await _operationReports08.FindLatestResumableAsync(source.Id, target.Id, mode);
            ResumeCloneButton.IsEnabled = result.Plan.CanExecute && resumable is not null;

            if (result.Plan.CanExecute)
            {
                AddLog("success", resumable is null
                    ? "Preflight passed. The operation is ready to start."
                    : $"Preflight passed. Resumable operation found: {resumable.Id}.");
            }
            else
            {
                AddLog("error", $"Preflight blocked the operation with {result.Plan.BlockingIssues.Count} issue(s).");
            }
        }
        catch (Exception exception)
        {
            CloneButton.IsEnabled = false;
            ResumeCloneButton.IsEnabled = false;
            PlanText.Text = exception.Message;
            AddLog("error", exception.Message);
        }
    }

    private static string BuildPlanWithPreflight08(ClonePlan plan, DiscordPreflightReport preflight)
    {
        var blocks = plan.BlockingIssues.Count == 0
            ? "None"
            : string.Join("\n• ", plan.BlockingIssues);
        var warnings = plan.Warnings.Count == 0
            ? "None"
            : string.Join("\n• ", plan.Warnings.Distinct(StringComparer.OrdinalIgnoreCase));
        return
            $"Plan: {plan.Id}\n" +
            $"Mode: {plan.Mode}  •  Risk: {plan.RiskScore}/100\n" +
            $"Bot: {preflight.BotName}  •  Highest role position: {preflight.HighestRolePosition}\n\n" +
            $"Roles: {plan.RolesToCreate} create, {plan.RolesToUpdate} update, {plan.RolesToReuse} reuse\n" +
            $"Channels: {plan.ChannelsToCreate} create, {plan.ChannelsToUpdate} update, {plan.ChannelsToReuse} reuse\n" +
            $"Emojis: {plan.EmojisToCreate} create, {plan.EmojisToReuse} reuse\n" +
            $"Removals: {plan.TargetRolesToDelete} roles, {plan.TargetChannelsToDelete} channels, {plan.TargetEmojisToDelete} emojis\n\n" +
            $"Blocking issues:\n• {blocks}\n\n" +
            $"Warnings:\n• {warnings}";
    }

    private async Task InitializeDevices08Async()
    {
        try
        {
            var claim = await _auth.ClaimCurrentDeviceAsync(_session);
            DeviceLimitText.Text = $"{claim.ActiveDevices} of {claim.DeviceLimit} licensed device slot(s) in use.";
            await RefreshDevices08Async();
        }
        catch (Exception exception)
        {
            DeviceLimitText.Text = "Device registration could not be confirmed: " + exception.Message;
            AddLog("warning", DeviceLimitText.Text);
        }
    }

    private async void RefreshDevices08_Click(object sender, RoutedEventArgs e) =>
        await RefreshDevices08Async();

    private async Task RefreshDevices08Async()
    {
        try
        {
            _devices08 = await _auth.GetDevicesAsync(_session);
            DevicesList.ItemsSource = _devices08.Select(device =>
                $"{(device.Active ? "●" : "○")}  {device.Name}\n" +
                $"First seen {device.FirstSeenAt.LocalDateTime:g}  •  Last seen {device.LastSeenAt.LocalDateTime:g}").ToList();
            var active = _devices08.Count(device => device.Active);
            DeviceLimitText.Text = $"{active} of {_session.License.DeviceLimit} licensed device slot(s) in use.";
            RevokeDeviceButton08.IsEnabled = DevicesList.SelectedIndex >= 0 &&
                                             DevicesList.SelectedIndex < _devices08.Count &&
                                             _devices08[DevicesList.SelectedIndex].Active;
        }
        catch (Exception exception)
        {
            DeviceLimitText.Text = "Could not load licensed devices: " + exception.Message;
            RevokeDeviceButton08.IsEnabled = false;
        }
    }

    private void DevicesList_SelectionChanged08(object sender, SelectionChangedEventArgs e)
    {
        RevokeDeviceButton08.IsEnabled = DevicesList.SelectedIndex >= 0 &&
                                         DevicesList.SelectedIndex < _devices08.Count &&
                                         _devices08[DevicesList.SelectedIndex].Active;
    }

    private async void RevokeDevice08_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedIndex < 0 || DevicesList.SelectedIndex >= _devices08.Count) return;
        var device = _devices08[DevicesList.SelectedIndex];
        if (!device.Active) return;

        if (MessageBox.Show(
                $"Remove '{device.Name}' from this license? Any active session from that device will end immediately.",
                "Remove licensed device",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await _auth.RevokeDeviceAsync(_session, device.Id);
            AddOperation("Licensed device removed: " + device.Name);
            await RefreshDevices08Async();

            try
            {
                await _auth.GetCurrentSessionAsync(_session);
            }
            catch
            {
                MessageBox.Show(
                    "The current computer was removed, so this session has ended.",
                    "GuildSync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LogoutRequested = true;
                Close();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Device management", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetSelectedUserDevices08_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin || AdminUsersList.SelectedIndex < 0 || AdminUsersList.SelectedIndex >= _adminUsers.Count)
        {
            MessageBox.Show("Select a user first.", "Administration", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var user = _adminUsers[AdminUsersList.SelectedIndex];
        if (MessageBox.Show(
                $"Remove every licensed device and active session for {user.Email}?",
                "Reset devices",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await _auth.ResetUserDevicesAsync(_session, user.Id);
            AddOperation("Admin reset devices: " + user.Email);
            await LoadAdminUsersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}