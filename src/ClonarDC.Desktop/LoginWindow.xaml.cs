using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class LoginWindow : Window
{
    private readonly AuthClient _auth = new();
    public AppSession? Session { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        PendingBox.Visibility = Visibility.Collapsed;
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = LocalizationService.T("Enter your email and password.");
            return;
        }

        var isDeveloperEmail = DeveloperAccess.IsDeveloperEmail(email);
        var isDeveloper = DeveloperAccess.Verify(email, password);
        if (isDeveloperEmail && !isDeveloper)
        {
            StatusText.Text = LocalizationService.T("Email or password is incorrect.");
            PasswordBox.Clear();
            return;
        }

        SetBusy(true, LocalizationService.T(isDeveloper ? "Opening developer mode…" : "Signing in…"));
        try
        {
            var session = await _auth.LoginAsync(email, password, bootstrapDeveloper: isDeveloper);
            if (isDeveloper && !session.IsAdmin)
                throw new InvalidOperationException("The main account did not receive administrator authorization.");

            if (session.License.Status is "pending" or "none")
            {
                PendingBox.Visibility = Visibility.Visible;
                StatusText.Text = LocalizationService.T("Your account exists, but it does not have an active license yet.");
                return;
            }
            if (session.License.Status is "suspended" or "revoked" or "expired")
            {
                StatusText.Text = $"Access unavailable: {session.License.Status}.";
                return;
            }

            Session = session;
            PasswordBox.Clear();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Unable to sign in. " + ex.Message;
            if (ex.Message.Contains("pend", StringComparison.OrdinalIgnoreCase)) PendingBox.Visibility = Visibility.Visible;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RegisterWindow(_auth) { Owner = this };
        var created = dialog.ShowDialog();
        if (created == true)
        {
            EmailBox.Text = dialog.RegisteredEmail;
            PendingBox.Visibility = Visibility.Visible;
            StatusText.Text = LocalizationService.T("Account created successfully. It is now waiting for approval.");
        }
    }

    private void SetBusy(bool busy, string? text = null)
    {
        LoginButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}