using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClonarDC.Services;

namespace ClonarDC;

public partial class LoginWindow : Window
{
    private const string LocalDeveloperUrl = "http://127.0.0.1:8787";
    private AuthClient _auth;
    private bool _introPlayed;
    private bool _hoverWired;

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
        BrandMarkHost.Content = BrandPresentation.CreateMark(178);
        CardMarkHost.Content = BrandPresentation.CreateMark(28, glow: false);
        UpdateBackendModeText();
        LocalizationService.Apply(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_hoverWired)
        {
            _hoverWired = true;
            LoginCard.MouseEnter += (_, _) => AnimateLoginCard(1.025, 170);
            LoginCard.MouseLeave += (_, _) => AnimateLoginCard(1.0, 220);
        }

        if (!_introPlayed)
        {
            _introPlayed = true;
            if (FindResource("LoginIntroStoryboard") is Storyboard intro)
                intro.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => EmailBox.Focus()));
    }

    private void AnimateLoginCard(double scale, int milliseconds)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        LoginCardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        LoginCardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
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
                    SwitchToLocalService();

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
            else if (!isDeveloper && _auth.UsesCentralBackend)
            {
                try
                {
                    session = await _auth.LoginAsync(email, password);
                }
                catch (Exception centralFailure) when (CanUseLocalPreviewFallback(centralFailure))
                {
                    StatusText.Text = LocalizationService.CurrentCode == "pt-BR"
                        ? "Serviço central indisponível. Verificando sua conta local de pré-visualização…"
                        : "Central service unavailable. Checking your local preview account…";
                    SwitchToLocalService();

                    try
                    {
                        session = await _auth.LoginAsync(email, password);
                    }
                    catch (Exception localFailure)
                    {
                        Environment.SetEnvironmentVariable("GUILDSYNC_API", null);
                        throw new InvalidOperationException(
                            $"Central login failed: {centralFailure.Message} Local preview login also failed: {localFailure.Message}");
                    }
                }
            }
            else
            {
                session = await _auth.LoginAsync(email, password, bootstrapDeveloper: isDeveloper);
            }

            if (isDeveloper && !session.IsAdmin)
                throw new InvalidOperationException("The main account did not receive administrator authorization.");

            var localPreviewAccount = !_auth.UsesCentralBackend && !isDeveloper;
            if (session.License.Status is "pending" or "none")
            {
                if (!localPreviewAccount)
                {
                    PendingBox.Visibility = Visibility.Visible;
                    StatusText.Text = LocalizationService.T("Your account exists, but it does not have an active license yet.");
                    return;
                }

                session = session with { License = new LicenseInfo("preview", null, 1) };
                IsLocalDeveloperSession = true;
                BackendModeText.Text = LocalizationService.CurrentCode == "pt-BR"
                    ? "MODO DE PRÉ-VISUALIZAÇÃO LOCAL — use o aplicativo agora; alterações reais exigem uma conexão Discord aceita."
                    : "LOCAL PREVIEW MODE — use the app now; real changes require an accepted Discord connection.";
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
            if (ex.Message.Contains("pend", StringComparison.OrdinalIgnoreCase))
                PendingBox.Visibility = Visibility.Visible;
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
        if (created != true) return;

        EmailBox.Text = dialog.RegisteredEmail;
        if (dialog.UsedLocalFallback)
        {
            SwitchToLocalService();
            PendingBox.Visibility = Visibility.Collapsed;
            StatusText.Text = LocalizationService.CurrentCode == "pt-BR"
                ? "Conta local criada. Digite a senha para abrir o modo de pré-visualização agora."
                : "Local account created. Enter the password to open preview mode now.";
            BackendModeText.Text = LocalizationService.CurrentCode == "pt-BR"
                ? "Serviço central indisponível — conta local de pré-visualização pronta."
                : "Central service unavailable — local preview account ready.";
        }
        else
        {
            PendingBox.Visibility = Visibility.Visible;
            StatusText.Text = LocalizationService.T("Account created successfully. It is now waiting for approval.");
        }

        PasswordBox.Focus();
    }

    private static bool CanUseLocalPreviewFallback(Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException or HttpRequestException)
            return true;

        var text = exception.Message;
        if (text.Contains("incorret", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("senha", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("suspended", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("revoked", StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("serviço", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("service", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("host", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tempor", StringComparison.OrdinalIgnoreCase);
    }

    private void SwitchToLocalService()
    {
        Environment.SetEnvironmentVariable("GUILDSYNC_API", LocalDeveloperUrl);
        _auth.Dispose();
        _auth = new AuthClient(LocalDeveloperUrl);
        IsLocalDeveloperSession = true;
    }

    private void UpdateBackendModeText()
    {
        BackendModeText.Text = _auth.UsesCentralBackend
            ? "Connected to the GuildSync central service."
            : "Connected to the local GuildSync service.";
    }

    private void SetBusy(bool busy, string? text = null)
    {
        LoginButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}
