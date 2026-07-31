using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow : Window
{
    private readonly AppSession _session;
    private readonly DiscordService _discord = new();
    private readonly DiscordProviderCoordinator _discordProviders;
    private readonly BackupService _backups = new();
    private readonly SecureTokenStore _secureToken = new();
    private readonly AuthClient _auth = new();
    private ClonePlan? _currentPlan;
    private GuildSnapshot? _currentSourceSnapshot;
    private readonly ObservableCollection<string> _logs = [];
    private readonly ObservableCollection<string> _operations = [];
    private readonly ObservableCollection<string> _backupItems = [];
    private readonly List<string> _backupPaths = [];
    private List<AdminUserDto> _adminUsers = [];

    public MainWindow(AppSession session)
    {
        _session = session;
        _discordProviders = new DiscordProviderCoordinator(_discord);
        InitializeComponent();
        LocalizationService.Apply(this);
        UserNameText.Text = session.DisplayName;
        UserEmailText.Text = session.Email;
        VersionText.Text = "v0.8.3.3";
        AdminNav.Visibility = session.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        LogList.ItemsSource = _logs;
        OperationsList.ItemsSource = _operations;
        BackupList.ItemsSource = _backupItems;
        BackupPathText.Text = _backups.BackupDirectory;
        var saved = _secureToken.Load();
        if (saved is not null) TokenBox.Password = saved;
        RefreshDashboard();
        RefreshBackups();
        UpdateLicenseText();
    }

    protected override void OnClosed(EventArgs e)
    {
        _discord.Dispose();
        base.OnClosed(e);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var index)) Pages.SelectedIndex = index;
    }

    private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pages.SelectedIndex == 2) RefreshBackups();
        if (Pages.SelectedIndex == 6 && _session.IsAdmin) _ = LoadAdminUsersAsync();
    }

    private async void TestToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDiscordToken();
            AddLog("info", "Checking the configured data provider…");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var result = await _discordProviders.LoadGuildsAsync(timeout.Token);
            ApplyServerItems(result.Guilds);
            _discordPreviewMode = result.IsSimulated;
            _discordPreviewReason = result.FallbackReason ?? string.Empty;
            AddLog(result.IsSimulated ? "warning" : "success", result.IsSimulated
                ? "The simulated provider is active: " + result.FallbackReason
                : $"The official provider loaded {result.Guilds.Count} server(s)." );
        }
        catch (Exception ex)
        {
            AddLog("error", ex.Message);
            PlanText.Text = ex.Message;
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDiscordToken();
            var source = RequireGuild(SourceGuildBox, "source server");
            var target = RequireGuild(TargetGuildBox, "destination server");
            if (source.Id == target.Id) throw new InvalidOperationException("The source and destination servers cannot be the same.");
            var mode = SelectedMode();
            CloneButton.IsEnabled = false;
            PlanText.Text = "Analyzing structure and differences…";
            AddLog("info", $"Analyzing {source.Name} → {target.Name}…");
            _currentPlan = await _discord.AnalyzeAsync(source.Id, target.Id, mode);
            _currentSourceSnapshot = await _discord.CaptureAsync(source.Id, MakeProgress());
            PlanText.Text = BuildPlanText(_currentPlan);
            CloneButton.IsEnabled = true;
            AddLog("success", "Plan generated. Review it before running the operation.");
        }
        catch (Exception ex)
        {
            PlanText.Text = ex.Message;
            AddLog("error", ex.Message);
        }
    }

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _currentSourceSnapshot is null)
        {
            MessageBox.Show("Run an analysis before cloning.");
            return;
        }

        var target = RequireGuild(TargetGuildBox, "destination server");
        var warning = _currentPlan.IsDestructive
            ? "EXACT mode can remove existing channels and roles from the destination. A backup will be created first. In the next confirmation, enter the destination server name."
            : "The operation will create items on the destination server. Review the plan before continuing.";

        if (MessageBox.Show(warning, "Confirm cloning", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        if (_currentPlan.IsDestructive)
        {
            var confirm = new TextConfirmWindow(target.Name) { Owner = this };
            if (confirm.ShowDialog() != true) return;
        }

        CloneButton.IsEnabled = false;
        try
        {
            if (AutoBackupCheck.IsChecked == true || _currentPlan.IsDestructive)
            {
                AddLog("info", "Creating an automatic backup of the destination…");
                var targetSnapshot = await _discord.CaptureAsync(target.Id, MakeProgress());
                var path = await _backups.SaveAsync(targetSnapshot, $"Before cloning — {target.Name}", "Automatic safety backup", ["automatic", "pre-clone"]);
                AddLog("success", "Backup saved: " + Path.GetFileName(path));
                RefreshBackups();
            }

            AddOperation($"Cloning started: {_currentSourceSnapshot.Name} → {target.Name}");
            await _discord.ExecuteCloneAsync(_currentSourceSnapshot, target.Id, _currentPlan.Mode, MakeProgress());
            AddOperation($"Cloning completed: {_currentSourceSnapshot.Name} → {target.Name}");
            DashboardLastOperation.Text = $"Cloning → {target.Name}";
            MessageBox.Show("Operation completed. Run another analysis to validate the result.", "GuildSync", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            AddLog("warning", "Operation canceled.");
        }
        catch (Exception ex)
        {
            AddLog("error", ex.Message);
            AddOperation("Failure: " + ex.Message);
            MessageBox.Show(ex.Message, "Cloning failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloneButton.IsEnabled = true;
        }
    }

    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshBackups();
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe", _backups.BackupDirectory) { UseShellExecute = true });

    private async void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count)
        {
            BackupDetails.Text = "Select a backup.";
            return;
        }

        try
        {
            var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            BackupDetails.Text = $"{env.Name}\nServer: {env.Snapshot.Name}\nCreated: {env.Snapshot.CreatedAt.LocalDateTime:g}\nRoles: {env.Snapshot.Roles.Count}\nChannels: {env.Snapshot.Channels.Count}\nEmojis: {env.Snapshot.Emojis.Count}\nIntegrity: valid";
        }
        catch (Exception ex)
        {
            BackupDetails.Text = "Failure: " + ex.Message;
        }
    }

    private async void VerifyBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count)
        {
            BackupDetails.Text = "Select a backup before verifying it.";
            AddLog("warning", "Backup verification requires a selected backup.");
            return;
        }

        try
        {
            var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            BackupDetails.Text = $"{env.Name}\nServer: {env.Snapshot.Name}\nIntegrity: valid\nSHA-256 and payload limits verified.";
            AddLog("success", $"Backup '{env.Name}' passed integrity validation.");
        }
        catch (Exception ex)
        {
            BackupDetails.Text = "Invalid backup: " + ex.Message;
            AddLog("error", "Backup validation failed: " + ex.Message);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count) return;
        try
        {
            SetDiscordToken();
            var target = RequireGuild(TargetGuildBox, "destination server on the Cloning screen");
            var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            if (MessageBox.Show($"Restore '{env.Name}' to {target.Name}? A preventive backup will be created first.", "Restore backup", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var before = await _discord.CaptureAsync(target.Id, MakeProgress());
            await _backups.SaveAsync(before, $"Before restoring — {target.Name}", "Preventive backup", ["automatic", "pre-restore"]);
            await _discord.ExecuteCloneAsync(env.Snapshot, target.Id, "exact", MakeProgress());
            AddOperation($"Backup restored to {target.Name}: {env.Name}");
            MessageBox.Show("Restore completed.");
        }
        catch (Exception ex)
        {
            AddLog("error", ex.Message);
            MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        _secureToken.Clear();
        TokenBox.Password = string.Empty;
        _discordProviders.SetCredential(string.Empty);
        _discordPreviewMode = true;
        _discordPreviewReason = "The saved value was explicitly removed.";
        ProviderStatusText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "Provedor simulado • valor salvo removido deste computador"
            : "Simulated provider • saved value removed from this computer";
        PlanText.Text = LocalizationService.CurrentCode == "pt-BR"
            ? "O valor salvo foi removido. Você pode inserir qualquer texto novamente."
            : "The saved value was removed. You can enter any text again.";
        AddLog("success", PlanText.Text);
    }

    private async void LoadAdmin_Click(object sender, RoutedEventArgs e) => await LoadAdminUsersAsync();

    private async Task LoadAdminUsersAsync()
    {
        if (!_session.IsAdmin) return;
        try
        {
            _adminUsers = await _auth.GetUsersAsync(_session);
            AdminUsersList.ItemsSource = _adminUsers.Select(u => $"{u.Name}  •  {u.Email}  •  {u.Status}  •  {u.License}").ToList();
        }
        catch (Exception ex)
        {
            AddOperation("Admin: " + ex.Message);
        }
    }

    private void AdminUsersList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private async void ApproveUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("approve", SelectedLicense());
    private async void SuspendUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("suspend");
    private async void ReactivateUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("reactivate");
    private async void RevokeUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("revoke");

    private async Task RunAdminAction(string action, string? license = null)
    {
        if (AdminUsersList.SelectedIndex < 0 || AdminUsersList.SelectedIndex >= _adminUsers.Count)
        {
            MessageBox.Show("Select a user.");
            return;
        }

        try
        {
            await _auth.AdminActionAsync(_session, _adminUsers[AdminUsersList.SelectedIndex].Id, action, license);
            await LoadAdminUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetDiscordToken()
    {
        var rawValue = TokenBox.Password ?? string.Empty;
        _discordProviders.SetCredential(rawValue);
        if (RememberTokenCheck.IsChecked == true) _secureToken.Save(rawValue);
    }

    private static GuildSummary RequireGuild(ComboBox box, string name) =>
        box.SelectedItem as GuildSummary ?? throw new InvalidOperationException($"Select the {name}.");

    private string SelectedMode() => (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "safe";
    private string SelectedLicense() => (LicenseBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1m";

    private IProgress<OperationLog> MakeProgress() => new Progress<OperationLog>(l => AddLog(l.Level, l.Message));

    private void AddLog(string level, string message)
    {
        var prefix = level switch { "success" => "✓", "error" => "✕", "warning" => "!", _ => "•" };
        _logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {prefix}  {message}");
        while (_logs.Count > 400) _logs.RemoveAt(_logs.Count - 1);
    }

    private void AddOperation(string text)
    {
        _operations.Insert(0, $"{DateTime.Now:g}  —  {text}");
        while (_operations.Count > 200) _operations.RemoveAt(_operations.Count - 1);
    }

    private void RefreshBackups()
    {
        _backupPaths.Clear();
        _backupItems.Clear();
        foreach (var path in _backups.ListBackups())
        {
            _backupPaths.Add(path);
            _backupItems.Add($"{Path.GetFileNameWithoutExtension(path)}\n{File.GetLastWriteTime(path):g}");
        }
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        DashboardLicense.Text = _session.License.Status;
        DashboardBackups.Text = _backups.ListBackups().Count.ToString();
    }

    private void UpdateLicenseText()
    {
        var expiry = _session.License.ExpiresAt is null ? "No expiration date" : _session.License.ExpiresAt.Value.LocalDateTime.ToString("f");
        LicenseDetails.Text = $"Status: {_session.License.Status}\nExpiration: {expiry}\nDevice limit: {_session.License.DeviceLimit}\n\nLicense validation is performed by the backend; the client does not decide by itself whether an account is authorized.";
    }

    private static string BuildPlanText(ClonePlan p)
    {
        var warnings = p.Warnings.Count == 0 ? "No critical warnings detected." : string.Join("\n• ", p.Warnings);
        return $"Mode: {p.Mode}\nRoles to create: {p.RolesToCreate}\nChannels to create: {p.ChannelsToCreate}\nEmojis to process: {p.EmojisToCreate}\nRoles to remove: {p.TargetRolesToDelete}\nChannels to remove: {p.TargetChannelsToDelete}\n\nWarnings:\n• {warnings}";
    }
}