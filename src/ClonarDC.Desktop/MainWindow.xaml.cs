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
        InitializeComponent();
        UserNameText.Text = session.DisplayName;
        UserEmailText.Text = session.Email;
        VersionText.Text = "v0.3.0 alpha";
        AdminNav.Visibility = session.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        LogList.ItemsSource = _logs;
        OperationsList.ItemsSource = _operations;
        BackupList.ItemsSource = _backupItems;
        BackupPathText.Text = _backups.BackupDirectory;
        var saved = _secureToken.Load();
        if (!string.IsNullOrWhiteSpace(saved)) TokenBox.Password = saved;
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
            AddLog("info", "Validando Token…");
            var bot = await _discord.ValidateTokenAsync();
            AddLog("success", $"Token válido. Bot: {bot}");
            if (RememberTokenCheck.IsChecked == true) _secureToken.Save(TokenBox.Password);
            var guilds = await _discord.GetGuildsAsync();
            SourceGuildBox.ItemsSource = guilds;
            TargetGuildBox.ItemsSource = guilds.ToList();
            if (guilds.Count > 0) SourceGuildBox.SelectedIndex = 0;
            if (guilds.Count > 1) TargetGuildBox.SelectedIndex = 1;
            AddLog("info", $"{guilds.Count} servidor(es) acessíveis pelo bot.");
        }
        catch (Exception ex) { AddLog("error", ex.Message); MessageBox.Show(ex.Message, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDiscordToken();
            var source = RequireGuild(SourceGuildBox, "servidor original");
            var target = RequireGuild(TargetGuildBox, "servidor de destino");
            if (source.Id == target.Id) throw new InvalidOperationException("O servidor original e o destino não podem ser iguais.");
            var mode = SelectedMode();
            CloneButton.IsEnabled = false;
            PlanText.Text = "Analisando estrutura e diferenças…";
            AddLog("info", $"Analisando {source.Name} → {target.Name}…");
            _currentPlan = await _discord.AnalyzeAsync(source.Id, target.Id, mode);
            _currentSourceSnapshot = await _discord.CaptureAsync(source.Id, MakeProgress());
            PlanText.Text = BuildPlanText(_currentPlan);
            CloneButton.IsEnabled = true;
            AddLog("success", "Plano gerado. Revise antes de executar.");
        }
        catch (Exception ex) { PlanText.Text = ex.Message; AddLog("error", ex.Message); }
    }

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _currentSourceSnapshot is null) { MessageBox.Show("Faça uma análise antes de clonar."); return; }
        var target = RequireGuild(TargetGuildBox, "servidor de destino");
        var warning = _currentPlan.IsDestructive
            ? "O modo EXATO pode remover canais e cargos existentes do destino. Um backup será criado antes. Digite o nome do servidor de destino na confirmação seguinte."
            : "A operação criará itens no servidor de destino. Revise o plano antes de continuar.";
        if (MessageBox.Show(warning, "Confirmar clonagem", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
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
                AddLog("info", "Criando backup automático do destino…");
                var targetSnapshot = await _discord.CaptureAsync(target.Id, MakeProgress());
                var path = await _backups.SaveAsync(targetSnapshot, $"Antes de clonar — {target.Name}", "Backup automático de segurança", ["automatico", "pre-clone"]);
                AddLog("success", "Backup salvo: " + Path.GetFileName(path));
                RefreshBackups();
            }
            AddOperation($"Clonagem iniciada: {_currentSourceSnapshot.Name} → {target.Name}");
            await _discord.ExecuteCloneAsync(_currentSourceSnapshot, target.Id, _currentPlan.Mode, MakeProgress());
            AddOperation($"Clonagem concluída: {_currentSourceSnapshot.Name} → {target.Name}");
            DashboardLastOperation.Text = $"Clonagem → {target.Name}";
            MessageBox.Show("Operação concluída. Recomendamos executar uma nova análise para validar o resultado.", "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { AddLog("warning", "Operação cancelada."); }
        catch (Exception ex) { AddLog("error", ex.Message); AddOperation("Falha: " + ex.Message); MessageBox.Show(ex.Message, "Falha na clonagem", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { CloneButton.IsEnabled = true; }
    }

    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshBackups();
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe", _backups.BackupDirectory) { UseShellExecute = true });

    private async void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count) { BackupDetails.Text = "Selecione um backup."; return; }
        try
        {
            var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            BackupDetails.Text = $"{env.Name}\nServidor: {env.Snapshot.Name}\nCriado: {env.Snapshot.CreatedAt.LocalDateTime:g}\nCargos: {env.Snapshot.Roles.Count}\nCanais: {env.Snapshot.Channels.Count}\nEmojis: {env.Snapshot.Emojis.Count}\nIntegridade: válida";
        }
        catch (Exception ex) { BackupDetails.Text = "Falha: " + ex.Message; }
    }

    private async void VerifyBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count) return;
        try { var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]); MessageBox.Show($"Backup '{env.Name}' íntegro.", "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Backup inválido", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count) return;
        try
        {
            SetDiscordToken();
            var target = RequireGuild(TargetGuildBox, "servidor de destino na tela Clonagem");
            var env = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            if (MessageBox.Show($"Restaurar '{env.Name}' em {target.Name}? Um backup preventivo será criado primeiro.", "Restaurar backup", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var before = await _discord.CaptureAsync(target.Id, MakeProgress());
            await _backups.SaveAsync(before, $"Antes de restaurar — {target.Name}", "Backup preventivo", ["automatico", "pre-restore"]);
            await _discord.ExecuteCloneAsync(env.Snapshot, target.Id, "exact", MakeProgress());
            AddOperation($"Backup restaurado em {target.Name}: {env.Name}");
            MessageBox.Show("Restauração concluída.");
        }
        catch (Exception ex) { AddLog("error", ex.Message); MessageBox.Show(ex.Message, "Falha na restauração", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        _secureToken.Clear(); TokenBox.Password = ""; MessageBox.Show("Token salvo removido deste computador.");
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
        catch (Exception ex) { AddOperation("Admin: " + ex.Message); }
    }
    private void AdminUsersList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private async void ApproveUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("approve", SelectedLicense());
    private async void SuspendUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("suspend");
    private async void ReactivateUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("reactivate");
    private async void RevokeUser_Click(object sender, RoutedEventArgs e) => await RunAdminAction("revoke");

    private async Task RunAdminAction(string action, string? license = null)
    {
        if (AdminUsersList.SelectedIndex < 0 || AdminUsersList.SelectedIndex >= _adminUsers.Count) { MessageBox.Show("Selecione um usuário."); return; }
        try { await _auth.AdminActionAsync(_session, _adminUsers[AdminUsersList.SelectedIndex].Id, action, license); await LoadAdminUsersAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Administração", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SetDiscordToken()
    {
        if (string.IsNullOrWhiteSpace(TokenBox.Password)) throw new InvalidOperationException("Informe o Token.");
        _discord.SetToken(TokenBox.Password);
        if (RememberTokenCheck.IsChecked == true) _secureToken.Save(TokenBox.Password);
    }
    private static GuildSummary RequireGuild(ComboBox box, string name) => box.SelectedItem as GuildSummary ?? throw new InvalidOperationException($"Selecione o {name}.");
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
        _backupPaths.Clear(); _backupItems.Clear();
        foreach (var path in _backups.ListBackups()) { _backupPaths.Add(path); _backupItems.Add($"{Path.GetFileNameWithoutExtension(path)}\n{File.GetLastWriteTime(path):g}"); }
        RefreshDashboard();
    }
    private void RefreshDashboard()
    {
        DashboardLicense.Text = _session.License.Status;
        DashboardBackups.Text = _backups.ListBackups().Count.ToString();
    }
    private void UpdateLicenseText()
    {
        var expiry = _session.License.ExpiresAt is null ? "Sem data de expiração" : _session.License.ExpiresAt.Value.LocalDateTime.ToString("f");
        LicenseDetails.Text = $"Status: {_session.License.Status}\nExpiração: {expiry}\nLimite de dispositivos: {_session.License.DeviceLimit}\n\nAs validações de licença são feitas pelo backend; o cliente não decide sozinho se uma conta está autorizada.";
    }
    private static string BuildPlanText(ClonePlan p)
    {
        var warnings = p.Warnings.Count == 0 ? "Nenhum alerta crítico detectado." : string.Join("\n• ", p.Warnings);
        return $"Modo: {p.Mode}\nCargos a criar: {p.RolesToCreate}\nCanais a criar: {p.ChannelsToCreate}\nEmojis a processar: {p.EmojisToCreate}\nCargos a remover: {p.TargetRolesToDelete}\nCanais a remover: {p.TargetChannelsToDelete}\n\nAlertas:\n• {warnings}";
    }
}
