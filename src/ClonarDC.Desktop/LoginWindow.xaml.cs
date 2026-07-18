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
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        PendingBox.Visibility = Visibility.Collapsed;
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "Informe o e-mail e a senha.";
            return;
        }

        var isDeveloperEmail = DeveloperAccess.IsDeveloperEmail(email);
        var isDeveloper = DeveloperAccess.Verify(email, password);
        if (isDeveloperEmail && !isDeveloper)
        {
            StatusText.Text = "E-mail ou senha incorretos.";
            PasswordBox.Clear();
            return;
        }

        SetBusy(true, isDeveloper ? "Abrindo modo de desenvolvimento…" : "Entrando…");
        try
        {
            var session = await _auth.LoginAsync(email, password, bootstrapDeveloper: isDeveloper);
            if (isDeveloper && !session.IsAdmin)
                throw new InvalidOperationException("A conta principal não recebeu autorização administrativa.");

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
            PasswordBox.Clear();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível entrar. " + ex.Message;
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
            StatusText.Text = "Conta criada com sucesso. Agora ela está esperando autorização.";
        }
    }

    private void SetBusy(bool busy, string? text = null)
    {
        LoginButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}