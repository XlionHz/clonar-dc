using System.Windows;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class RegisterWindow : Window
{
    private readonly AuthClient _auth;
    private bool _submitting;

    public string RegisteredEmail { get; private set; } = "";

    public RegisterWindow(AuthClient auth)
    {
        _auth = auth;
        InitializeComponent();
        LocalizationService.Apply(this);
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting) return;

        HideFeedback();
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ShowError(LocalizationService.T("Fill in the name and email."));
            return;
        }

        if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
        {
            ShowError(LocalizationService.T("Enter a valid email address."));
            return;
        }

        if (password.Length < 8)
        {
            ShowError(LocalizationService.T("Use a password with at least 8 characters."));
            return;
        }

        if (password != ConfirmBox.Password)
        {
            ShowError(LocalizationService.T("The passwords do not match."));
            return;
        }

        _submitting = true;
        CreateButton.IsEnabled = false;
        CreateButton.Content = LocalizationService.T("Creating account…");
        BusyBar.Visibility = Visibility.Visible;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await _auth.RegisterAsync(name, email, password, timeout.Token);

            RegisteredEmail = email;
            FormPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
            LocalizationService.Apply(SuccessPanel);

            await Task.Delay(1600);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            ShowError(LocalizationService.T("The request took too long. Check your connection and try again."));
        }
        catch (Exception ex)
        {
            ShowError(FriendlyRegistrationError(ex.Message));
        }
        finally
        {
            if (DialogResult != true)
            {
                _submitting = false;
                CreateButton.IsEnabled = true;
                CreateButton.Content = LocalizationService.T("Create account");
                BusyBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void HideFeedback()
    {
        FeedbackBox.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void ShowError(string message)
    {
        BusyBar.Visibility = Visibility.Collapsed;
        FeedbackBox.Visibility = Visibility.Visible;
        FeedbackBox.Background = new SolidColorBrush(Color.FromRgb(36, 21, 29));
        FeedbackBox.BorderBrush = new SolidColorBrush(Color.FromRgb(251, 113, 133));
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 164, 175));
        StatusText.Text = message;
    }

    private static string FriendlyRegistrationError(string raw)
    {
        if (raw.Contains("Já existe uma conta", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.T("An account already exists with this email.");
        }

        if (raw.Contains("serviço de contas", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("account service", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.T("The account service could not be started. Restart the app and try again.");
        }

        return LocalizationService.T("We could not create your account. Check the information and try again.") +
               Environment.NewLine + raw;
    }
}