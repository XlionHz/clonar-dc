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
#if DEBUG
        DevButton.Visibility = Visibility.Visible;
#endif
        if (Environment.GetEnvironmentVariable("CLONARDC_DEV_MODE") == "1") DevButton.Visibility = Visibility.Visible;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Entrando…");
        try
        {
            var session = await _auth.LoginAsync(EmailBox.Text.Trim(), PasswordBox.Password);
            if (session.License.Status is "pending" or "none")
            {
                PendingBox.Visibility = Visibility.Visible;
                StatusText.Text = "Sua conta existe, mas ainda não possui uma licença ativa.";
                return;
            }
            if (session.License.Status is "suspended" or "revoked" or "expired")
            {
                StatusText.Text = $"Acesso indisponível: {session.License.Status}.";
                return;
            }
            Session = session;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível entrar. " + ex.Message;
            if (ex.Message.Contains("pend", StringComparison.OrdinalIgnoreCase)) PendingBox.Visibility = Visibility.Visible;
        }
        finally { SetBusy(false); }
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RegisterWindow(_auth) { Owner = this };
        var created = dialog.ShowDialog();
        if (created == true)
        {
            EmailBox.Text = dialog.RegisteredEmail;
            PendingBox.Visibility = Visibility.Visible;
            StatusText.Text = "Conta criada com sucesso.";
        }
        await Task.CompletedTask;
    }

    private void DevButton_Click(object sender, RoutedEventArgs e)
    {
        Session = new AppSession("dev@local", "Desenvolvimento", "admin", "local", LicenseInfo.Local);
        DialogResult = true;
    }

    private void SetBusy(bool busy, string? text = null)
    {
        LoginButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}
