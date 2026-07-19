using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private bool _automaticUpdateCheckStarted;
    private CancellationTokenSource? _updateCts;
    private Button? _checkUpdatesButton;
    private TextBlock? _updateStatusText;

    private void InitializeUpdateUi()
    {
        if (_checkUpdatesButton is not null) return;

        var settingsTab = Pages.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Settings", StringComparison.Ordinal));
        if (settingsTab?.Content is not StackPanel settingsPanel) return;

        var card = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 24, 0, 6)
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Updates"),
            FontSize = 18,
            FontWeight = FontWeights.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("The app checks for new versions automatically."),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 14)
        });

        _checkUpdatesButton = new Button
        {
            Content = LocalizationService.T("Check for updates"),
            Width = 230,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)FindResource("PrimaryButton")
        };
        _checkUpdatesButton.Click += CheckUpdates_Click;
        content.Children.Add(_checkUpdatesButton);

        _updateStatusText = new TextBlock
        {
            Text = LocalizationService.T("No update check has run yet."),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        content.Children.Add(_updateStatusText);
        card.Child = content;

        settingsPanel.Children.Insert(Math.Min(2, settingsPanel.Children.Count), card);
        _ = StartAutomaticUpdateCheckAsync();
    }

    private async Task StartAutomaticUpdateCheckAsync()
    {
        if (_automaticUpdateCheckStarted) return;
        _automaticUpdateCheckStarted = true;
        await Task.Delay(2500);
        await CheckForUpdatesAsync(showCurrentMessage: false);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showCurrentMessage: true);
    }

    private async Task CheckForUpdatesAsync(bool showCurrentMessage)
    {
        if (_updateCts is not null || _checkUpdatesButton is null || _updateStatusText is null) return;

        _updateCts = new CancellationTokenSource();
        _checkUpdatesButton.IsEnabled = false;
        _updateStatusText.Text = "Checking for updates…";

        try
        {
            var update = await _updateService.CheckAsync(_updateCts.Token);
            if (update is null)
            {
                _updateStatusText.Text = $"Current version: {_updateService.CurrentVersion}. No updates are available.";
                if (showCurrentMessage)
                    MessageBox.Show("You are already using the latest version.", "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _updateStatusText.Text = $"New version available: {update.Tag}.";
            var answer = MessageBox.Show(
                $"Version {update.Tag} is available.\n\nClonar DC can download, verify, and install it automatically. The app will close during installation.\n\nUpdate now?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes) return;

            var progress = new Progress<int>(percent =>
            {
                _updateStatusText.Text = $"Downloading update… {percent}%";
            });

            var setupPath = await _updateService.DownloadAndVerifyAsync(update, progress, _updateCts.Token);
            _updateStatusText.Text = "Download complete and integrity verified. Installing…";
            UpdateService.LaunchInstaller(setupPath);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _updateStatusText.Text = "Update check canceled.";
        }
        catch (Exception ex)
        {
            _updateStatusText.Text = "Unable to update: " + ex.Message;
            if (showCurrentMessage)
                MessageBox.Show(_updateStatusText.Text, LocalizationService.T("Updates"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkUpdatesButton.IsEnabled = true;
            _updateCts.Dispose();
            _updateCts = null;
        }
    }
}