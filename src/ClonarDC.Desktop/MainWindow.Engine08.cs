using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private readonly OperationReportService _operationReports08 = new();
    private readonly List<string> _operationReportPaths08 = [];
    private GuildSyncEngine? _engine08;
    private CancellationTokenSource? _operationCancellation08;
    private CloneExecutionReport? _selectedReport08;

    private async void Engine08_Loaded(object sender, RoutedEventArgs e)
    {
        _engine08 ??= new GuildSyncEngine(_discord);
        Closed += (_, _) =>
        {
            _operationCancellation08?.Cancel();
            _operationCancellation08?.Dispose();
            _engine08?.Dispose();
        };
        await RefreshOperationReports08Async();
    }

    private GuildSyncEngine RequireEngine08()
    {
        _engine08 ??= new GuildSyncEngine(_discord);
        _engine08.SetToken(TokenBox.Password);
        return _engine08;
    }

    private async void Analyze08_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var engine = RequireEngine08();
            var source = RequireGuild(SourceGuildBox, "source server");
            var target = RequireGuild(TargetGuildBox, "destination server");
            var mode = SelectedMode();
            CloneButton.IsEnabled = false;
            ResumeCloneButton.IsEnabled = false;
            PlanText.Text = "Analyzing the stable structure and operation risks…";
            AddLog("info", $"Analyzing {source.Name} → {target.Name} in {mode} mode…");

            var result = await engine.AnalyzeAsync(source.Id, target.Id, mode, MakeProgress());
            _currentPlan = result.Plan;
            _currentSourceSnapshot = result.Source;
            PlanText.Text = BuildPlanText08(result.Plan);
            CloneButton.IsEnabled = true;
            var resumable = await _operationReports08.FindLatestResumableAsync(source.Id, target.Id, mode);
            ResumeCloneButton.IsEnabled = resumable is not null;
            AddLog("success", resumable is null
                ? "Plan generated and ready for execution."
                : $"Plan generated. Resumable operation found: {resumable.Id}.");
        }
        catch (Exception exception)
        {
            PlanText.Text = exception.Message;
            AddLog("error", exception.Message);
        }
    }

    private async void Clone08_Click(object sender, RoutedEventArgs e) =>
        await RunCurrentClone08Async(resume: false);

    private async void ResumeClone08_Click(object sender, RoutedEventArgs e) =>
        await RunCurrentClone08Async(resume: true);

    private void CancelClone08_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation08 is null) return;
        CancelCloneButton.IsEnabled = false;
        AddLog("warning", "Cancellation requested. GuildSync will stop after the current Discord request finishes.");
        _operationCancellation08.Cancel();
    }

    private async Task RunCurrentClone08Async(bool resume)
    {
        if (_currentPlan is null || _currentSourceSnapshot is null)
        {
            MessageBox.Show("Run an analysis before starting the operation.", "GuildSync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = RequireGuild(TargetGuildBox, "destination server");
        CloneExecutionReport? resumeReport = null;
        if (resume)
        {
            resumeReport = await _operationReports08.FindLatestResumableAsync(
                _currentPlan.SourceGuildId,
                _currentPlan.TargetGuildId,
                _currentPlan.Mode);
            if (resumeReport is null)
            {
                MessageBox.Show("No compatible resumable operation was found.", "GuildSync", MessageBoxButton.OK, MessageBoxImage.Information);
                ResumeCloneButton.IsEnabled = false;
                return;
            }
        }
        else
        {
            var warning = _currentPlan.IsDestructive
                ? "EXACT mode removes clonable roles, channels and emojis from the destination. GuildSync will create a preventive backup first."
                : "GuildSync will create or update the items described in the operation plan.";
            if (MessageBox.Show(warning, "Confirm operation", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            if (_currentPlan.IsDestructive)
            {
                var confirmation = new TextConfirmWindow(target.Name) { Owner = this };
                if (confirmation.ShowDialog() != true) return;
            }
        }

        var engine = RequireEngine08();
        SetOperationButtons08(running: true);
        _operationCancellation08 = new CancellationTokenSource();
        try
        {
            if (!resume && (AutoBackupCheck.IsChecked == true || _currentPlan.IsDestructive))
            {
                AddLog("info", "Creating a preventive backup of the destination…");
                var targetSnapshot = await _discord.CaptureAsync(target.Id, MakeProgress(), _operationCancellation08.Token);
                var backupPath = await _backups.SaveAsync(
                    targetSnapshot,
                    $"Before operation — {target.Name}",
                    $"Automatic backup before plan {_currentPlan.Id}",
                    ["automatic", "pre-operation", _currentPlan.Mode],
                    _operationCancellation08.Token);
                AddLog("success", "Preventive backup saved: " + Path.GetFileName(backupPath));
                RefreshBackups();
            }

            var options = new CloneExecutionOptions
            {
                Mode = _currentPlan.Mode,
                ContinueOnError = true,
                VerifyAfterExecution = true,
                ReorderResources = true
            };
            AddOperation($"Operation {(resume ? "resumed" : "started")}: {_currentSourceSnapshot.Name} → {target.Name}");
            var report = await engine.ExecuteAsync(
                _currentSourceSnapshot,
                target,
                options,
                resumeReport,
                (value, token) => _operationReports08.SaveAsync(value, token),
                MakeProgress(),
                _operationCancellation08.Token);

            var summaryPath = await _operationReports08.ExportSummaryAsync(report);
            AddOperation($"Operation {report.Status}: {report.Id}");
            DashboardLastOperation.Text = $"{FormatReportStatus08(report.Status)} → {target.Name}";
            await RefreshOperationReports08Async(report.Id);
            MessageBox.Show(
                $"Operation status: {FormatReportStatus08(report.Status)}\n" +
                $"Completed steps: {report.CompletedSteps}/{report.Steps.Count}\n" +
                $"Failed steps: {report.FailedSteps}\n\n" +
                $"Summary: {summaryPath}",
                "GuildSync operation report",
                MessageBoxButton.OK,
                report.Status == "completed" ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            AddLog("warning", "Operation cancelled. Its checkpoint remains available on the Operations page.");
            AddOperation("Operation cancelled — resumable checkpoint saved");
            await RefreshOperationReports08Async();
        }
        catch (CloneExecutionException exception)
        {
            AddLog("error", exception.Message);
            AddOperation($"Operation failed: {exception.Report.Id}");
            await RefreshOperationReports08Async(exception.Report.Id);
            MessageBox.Show(
                $"{exception.Message}\n\nReport: {exception.Report.Id}",
                "GuildSync operation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            AddLog("error", exception.Message);
            AddOperation("Failure: " + exception.Message);
            MessageBox.Show(exception.Message, "GuildSync operation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation08?.Dispose();
            _operationCancellation08 = null;
            SetOperationButtons08(running: false);
        }
    }

    private async void RestoreBackup08_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || BackupList.SelectedIndex >= _backupPaths.Count) return;
        try
        {
            var engine = RequireEngine08();
            var target = RequireGuild(TargetGuildBox, "destination server on the Cloning screen");
            var envelope = await _backups.LoadAsync(_backupPaths[BackupList.SelectedIndex]);
            if (MessageBox.Show(
                    $"Restore '{envelope.Name}' to {target.Name}? The destination structure will be recreated and a preventive backup will be made first.",
                    "Restore backup",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var confirmation = new TextConfirmWindow(target.Name) { Owner = this };
            if (confirmation.ShowDialog() != true) return;

            SetOperationButtons08(running: true);
            _operationCancellation08 = new CancellationTokenSource();
            var before = await _discord.CaptureAsync(target.Id, MakeProgress(), _operationCancellation08.Token);
            await _backups.SaveAsync(
                before,
                $"Before restore — {target.Name}",
                $"Preventive backup before restoring {envelope.Id}",
                ["automatic", "pre-restore"],
                _operationCancellation08.Token);

            var report = await engine.ExecuteAsync(
                envelope.Snapshot,
                target,
                new CloneExecutionOptions { Mode = "exact", ContinueOnError = true, VerifyAfterExecution = true },
                null,
                (value, token) => _operationReports08.SaveAsync(value, token),
                MakeProgress(),
                _operationCancellation08.Token);
            await _operationReports08.ExportSummaryAsync(report);
            AddOperation($"Backup restore {report.Status}: {envelope.Name} → {target.Name}");
            RefreshBackups();
            await RefreshOperationReports08Async(report.Id);
            MessageBox.Show(
                report.Status == "completed"
                    ? "Restore completed and verified."
                    : "Restore finished with differences. Review the operation report.",
                "GuildSync restore",
                MessageBoxButton.OK,
                report.Status == "completed" ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            AddLog("warning", "Restore cancelled. A resumable checkpoint was saved.");
            await RefreshOperationReports08Async();
        }
        catch (Exception exception)
        {
            AddLog("error", exception.Message);
            MessageBox.Show(exception.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation08?.Dispose();
            _operationCancellation08 = null;
            SetOperationButtons08(running: false);
        }
    }

    private async void OperationsList_SelectionChanged08(object sender, SelectionChangedEventArgs e)
    {
        _selectedReport08 = null;
        ResumeSelectedOperationButton.IsEnabled = false;
        OpenOperationReportButton.IsEnabled = false;
        if (OperationsList.SelectedIndex < 0 || OperationsList.SelectedIndex >= _operationReportPaths08.Count)
        {
            OperationDetailsText.Text = "Select an operation report.";
            return;
        }

        try
        {
            _selectedReport08 = await _operationReports08.LoadAsync(_operationReportPaths08[OperationsList.SelectedIndex]);
            ShowReport08(_selectedReport08);
            ResumeSelectedOperationButton.IsEnabled = _selectedReport08.CanResume;
            OpenOperationReportButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            OperationDetailsText.Text = "Could not read this report: " + exception.Message;
        }
    }

    private async void ResumeSelectedOperation08_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedReport08 is null || !_selectedReport08.CanResume) return;
        try
        {
            var engine = RequireEngine08();
            var source = await _discord.CaptureAsync(_selectedReport08.SourceGuildId, MakeProgress());
            var target = new GuildSummary(_selectedReport08.TargetGuildId, _selectedReport08.TargetGuildName, null);
            _currentSourceSnapshot = source;
            _currentPlan = new ClonePlan
            {
                SourceGuildId = source.SourceGuildId,
                SourceGuildName = source.Name,
                TargetGuildId = target.Id,
                TargetGuildName = target.Name,
                Mode = _selectedReport08.Mode
            };

            SetOperationButtons08(running: true);
            _operationCancellation08 = new CancellationTokenSource();
            var report = await engine.ExecuteAsync(
                source,
                target,
                new CloneExecutionOptions { Mode = _selectedReport08.Mode, ContinueOnError = true, VerifyAfterExecution = true },
                _selectedReport08,
                (value, token) => _operationReports08.SaveAsync(value, token),
                MakeProgress(),
                _operationCancellation08.Token);
            await _operationReports08.ExportSummaryAsync(report);
            await RefreshOperationReports08Async(report.Id);
            MessageBox.Show($"Operation resumed with status: {FormatReportStatus08(report.Status)}.", "GuildSync", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            await RefreshOperationReports08Async(_selectedReport08.Id);
        }
        catch (Exception exception)
        {
            AddLog("error", exception.Message);
            MessageBox.Show(exception.Message, "Resume failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation08?.Dispose();
            _operationCancellation08 = null;
            SetOperationButtons08(running: false);
        }
    }

    private async void OpenOperationReport08_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedReport08 is null) return;
        var summary = await _operationReports08.ExportSummaryAsync(_selectedReport08);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{summary}\"") { UseShellExecute = true });
    }

    private async void RefreshOperations08_Click(object sender, RoutedEventArgs e) =>
        await RefreshOperationReports08Async(_selectedReport08?.Id);

    private async Task RefreshOperationReports08Async(string? selectReportId = null)
    {
        _operationReportPaths08.Clear();
        _operations.Clear();
        var selectedIndex = -1;
        foreach (var path in _operationReports08.ListReports())
        {
            try
            {
                var report = await _operationReports08.LoadAsync(path);
                _operationReportPaths08.Add(path);
                _operations.Add(
                    $"{report.StartedAt.LocalDateTime:g}  •  {FormatReportStatus08(report.Status)}\n" +
                    $"{report.SourceGuildName} → {report.TargetGuildName}  •  {report.CompletedSteps}/{report.Steps.Count} steps");
                if (report.Id == selectReportId) selectedIndex = _operations.Count - 1;
            }
            catch
            {
                _operationReportPaths08.Add(path);
                _operations.Add($"Unreadable operation report\n{Path.GetFileName(path)}");
            }
        }
        if (selectedIndex >= 0) OperationsList.SelectedIndex = selectedIndex;
        else if (_operations.Count > 0 && OperationsList.SelectedIndex < 0) OperationsList.SelectedIndex = 0;
    }

    private void ShowReport08(CloneExecutionReport report)
    {
        var verification = report.Verification is null
            ? "Not run"
            : report.Verification.Passed
                ? "Passed"
                : $"Differences: {report.Verification.Differences.Count}";
        OperationDetailsText.Text =
            $"ID: {report.Id}\n" +
            $"Status: {FormatReportStatus08(report.Status)}\n" +
            $"Source: {report.SourceGuildName}\n" +
            $"Target: {report.TargetGuildName}\n" +
            $"Mode: {report.Mode}\n" +
            $"Started: {report.StartedAt.LocalDateTime:g}\n" +
            $"Completed steps: {report.CompletedSteps}/{report.Steps.Count}\n" +
            $"Failed steps: {report.FailedSteps}\n" +
            $"Verification: {verification}\n\n" +
            string.Join("\n", report.Errors.Take(8).Select(value => "• " + value));
    }

    private void SetOperationButtons08(bool running)
    {
        CloneButton.IsEnabled = !running && _currentPlan is not null;
        ResumeCloneButton.IsEnabled = !running && _currentPlan is not null;
        CancelCloneButton.IsEnabled = running;
        AnalyzeEngineButton.IsEnabled = !running;
        RestoreBackupButton08.IsEnabled = !running;
    }

    private static string BuildPlanText08(ClonePlan plan)
    {
        var warnings = plan.Warnings.Count == 0 ? "No critical warnings detected." : string.Join("\n• ", plan.Warnings);
        return
            $"Plan: {plan.Id}\n" +
            $"Mode: {plan.Mode}  •  Risk: {plan.RiskScore}/100\n\n" +
            $"Roles: {plan.RolesToCreate} create, {plan.RolesToUpdate} update, {plan.RolesToReuse} reuse\n" +
            $"Channels: {plan.ChannelsToCreate} create, {plan.ChannelsToUpdate} update, {plan.ChannelsToReuse} reuse\n" +
            $"Emojis: {plan.EmojisToCreate} create, {plan.EmojisToReuse} reuse\n" +
            $"Removals: {plan.TargetRolesToDelete} roles, {plan.TargetChannelsToDelete} channels, {plan.TargetEmojisToDelete} emojis\n\n" +
            $"Warnings:\n• {warnings}";
    }

    private static string FormatReportStatus08(string status) => status switch
    {
        "completed" => "Completed and verified",
        "completed-with-errors" => "Completed with errors",
        "completed-with-differences" => "Completed with differences",
        "cancelled" => "Cancelled — resumable",
        "failed" => "Failed — resumable",
        "running" => "In progress",
        _ => status
    };
}