using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class LoginWindow : Window
{
    private const string LocalDeveloperUrl = "http://127.0.0.1:8787";
    private AuthClient _auth;

    public AppSession? Session { get; private set; }
    public bool IsLocalDeveloperSession { get; private set; }

    public LoginWindow()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("GUILDSYNC_API"),
                LocalDeveloperUrl,
                StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("GUILDSYNC_API", null);

        _auth = new AuthClient();
        InitializeComponent();
        BrandMarkHost.Content = BrandPresentation.CreateMark(138);
        BackendModeText.Text = _auth.UsesCentralBackend
            ? "Connected to the GuildSync central service."
            : "Connected to the local GuildSync service.";
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

        SetBusy(true, LocalizationService.T(isDeveloper ? "Opening administrator access…" : "Signing in…"));
        try
        {
            AppSession session;
            if (isDeveloper && _auth.UsesCentralBackend)
            {
                try
                {
                    session = await _auth.LoginAsync(email, password);
                    if (!session.IsAdmin)
                        throw new InvalidOperationException("The central account is not authorized as an administrator.");
                }
                catch (Exception centralFailure)
                {
                    StatusText.Text = "Central administrator access is unavailable. Opening the isolated local developer service…";
                    Environment.SetEnvironmentVariable("GUILDSYNC_API", LocalDeveloperUrl);
                    _auth.Dispose();
                    _auth = new AuthClient(LocalDeveloperUrl);

                    try
                    {
                        session = await _auth.LoginAsync(email, password, bootstrapDeveloper: true);
                    }
                    catch (Exception localFailure)
                    {
                        Environment.SetEnvironmentVariable("GUILDSYNC_API", null);
                        throw new InvalidOperationException(
                            $"Central administrator login failed: {centralFailure.Message} Local developer login also failed: {localFailure.Message}");
                    }

                    IsLocalDeveloperSession = true;
                    BackendModeText.Text = "LOCAL DEVELOPER MODE — this administration panel uses an isolated database, not the public GuildSync service.";
                }
            }
            else
            {
                session = await _auth.LoginAsync(email, password, bootstrapDeveloper: isDeveloper);
            }

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
