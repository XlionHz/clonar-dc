using System.Diagnostics;
using System.Windows;
using ClonarDC.Services;
using Microsoft.Win32;

namespace ClonarDC;

public partial class PublishUpdateWindow : Window
{
    private readonly UpdatePackagePublisher _publisher = new();
    private readonly SecureTokenStore _publishingKeyStore = new("github-publisher.dat", "Clonar DC GitHub publisher");
    private UpdatePackageInfo? _package;
    private bool _publishing;

    public PublishUpdateWindow()
    {
        InitializeComponent();
        ApplyCopy();

        var saved = _publishingKeyStore.Load();
        if (!string.IsNullOrWhiteSpace(saved))
        {
            PublishingKeyBox.Password = saved;
            RememberKeyCheck.IsChecked = true;
        }
    }

    private async void ChoosePackage_Click(object sender, RoutedEventArgs e)
    {
        if (_publishing) return;

        var dialog = new OpenFileDialog
        {
            Title = Copy("choose-dialog"),
            Filter = "Clonar DC update package (*.clonardc-update)|*.clonardc-update|ZIP package (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, Copy("checking"));
        try
        {
            _package = await _publisher.InspectAsync(dialog.FileName);
            PackagePathBox.Text = dialog.FileName;
            PackageVersionText.Text = Copy("version") + ": " + _package.Version;
            PackageTitleText.Text = _package.Title;
            PackageSizeText.Text = Copy("size") + ": " + FormatSize(_package.SetupSize) + "  •  SHA-256 " + _package.Sha256[..12] + "…";
            PackageNotesText.Text = _package.Notes;
            PackageInfoBox.Visibility = Visibility.Visible;
            StatusText.Text = Copy("verified");
        }
        catch (Exception ex)
        {
            _package = null;
            PackageInfoBox.Visibility = Visibility.Collapsed;
            PackagePathBox.Text = dialog.FileName;
            StatusText.Text = Copy("invalid") + Environment.NewLine + ex.Message;
        }
        finally
        {
            SetBusy(false);
            UpdatePublishAvailability();
        }
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (_publishing || _package is null) return;
        if (string.IsNullOrWhiteSpace(PublishingKeyBox.Password))
        {
            StatusText.Text = Copy("key-required");
            return;
        }

        var answer = MessageBox.Show(
            Copy("confirm-body").Replace("{version}", _package.Version.ToString(), StringComparison.Ordinal),
            Copy("confirm-title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        _publishing = true;
        SetBusy(true, Copy("starting"));
        var progress = new Progress<string>(message => StatusText.Text = LocalizeProgress(message));

        try
        {
            var key = PublishingKeyBox.Password;
            var releaseUrl = await _publisher.PublishAsync(_package, key, progress);

            if (RememberKeyCheck.IsChecked == true) _publishingKeyStore.Save(key);
            else _publishingKeyStore.Clear();

            StatusText.Text = Copy("published");
            var open = MessageBox.Show(
                Copy("published-body"),
                Copy("published-title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = Copy("publish-failed") + Environment.NewLine + ex.Message;
            MessageBox.Show(StatusText.Text, "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _publishing = false;
            SetBusy(false);
            UpdatePublishAvailability();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_publishing)
        {
            MessageBox.Show(Copy("publishing-active"), "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Close();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        PublishProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ChoosePackageButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        PublishingKeyBox.IsEnabled = !busy;
        RememberKeyCheck.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private void UpdatePublishAvailability()
    {
        PublishButton.IsEnabled = !_publishing && _package is not null;
    }

    private void ApplyCopy()
    {
        Title = Copy("window-title");
        TitleText.Text = Copy("title");
        SubtitleText.Text = Copy("subtitle");
        PackageHeadingText.Text = Copy("package-heading");
        PackageHelpText.Text = Copy("package-help");
        ChoosePackageButton.Content = Copy("choose");
        AccessHeadingText.Text = Copy("access-heading");
        AccessHelpText.Text = Copy("access-help");
        RememberKeyCheck.Content = Copy("remember");
        SecurityNoticeText.Text = Copy("security");
        CancelButton.Content = Copy("close");
        PublishButton.Content = Copy("publish");
    }

    private static string LocalizeProgress(string english)
    {
        if (LocalizationService.CurrentCode != "pt-BR") return english;
        return english switch
        {
            "Checking the package version against the latest published release…" => "Comparando a versão do pacote com a última publicação…",
            "Creating a private draft release…" => "Criando uma publicação privada temporária…",
            "Uploading the installer… This may take a few minutes." => "Enviando o instalador… Isso pode levar alguns minutos.",
            "Uploading the SHA-256 verification file…" => "Enviando o arquivo de verificação SHA-256…",
            "Publishing the release to all installations…" => "Publicando a atualização para todas as instalações…",
            _ => english
        };
    }

    private static string FormatSize(long bytes)
    {
        var mb = bytes / 1024d / 1024d;
        return $"{mb:0.0} MB";
    }

    private static string Copy(string key)
    {
        var pt = LocalizationService.CurrentCode == "pt-BR";
        if (pt)
        {
            return key switch
            {
                "window-title" => "Publicar atualização — Clonar DC",
                "title" => "Publicar atualização do app",
                "subtitle" => "Envie um único pacote verificado para todas as instalações do Clonar DC.",
                "package-heading" => "1. Pacote de atualização",
                "package-help" => "Selecione o arquivo .clonardc-update fornecido junto da nova versão.",
                "choose" => "Escolher arquivo",
                "access-heading" => "2. Chave de publicação",
                "access-help" => "Use um token detalhado do GitHub limitado ao repositório XlionHz/clonar-dc com Contents: Read and write. A chave nunca é colocada dentro do pacote.",
                "remember" => "Guardar esta chave criptografada para este usuário do Windows",
                "security" => "A publicação cria uma Release no GitHub. O app verifica o SHA-256 do pacote antes do envio; as instalações encontram a nova versão pelo atualizador já existente.",
                "close" => "Fechar",
                "publish" => "Publicar atualização para todos",
                "choose-dialog" => "Escolha o pacote de atualização do Clonar DC",
                "checking" => "Verificando pacote…",
                "version" => "Versão",
                "size" => "Tamanho",
                "verified" => "Pacote verificado. Revise os dados antes de publicar.",
                "invalid" => "O pacote não pôde ser validado.",
                "key-required" => "Informe a chave de publicação do GitHub.",
                "confirm-title" => "Publicar atualização",
                "confirm-body" => "A versão {version} será publicada e poderá ser baixada por todas as instalações do Clonar DC. Continuar?",
                "starting" => "Preparando publicação…",
                "published" => "Atualização publicada com sucesso. Os aplicativos detectarão a nova versão automaticamente.",
                "published-title" => "Atualização publicada",
                "published-body" => "A atualização já está disponível. Deseja abrir a página da publicação no GitHub?",
                "publish-failed" => "Não foi possível publicar a atualização.",
                "publishing-active" => "A publicação está em andamento. Aguarde a conclusão antes de fechar esta janela.",
                _ => key
            };
        }

        return key switch
        {
            "window-title" => "Publish app update — Clonar DC",
            "title" => "Publish app update",
            "subtitle" => "Send one verified package to every Clonar DC installation.",
            "package-heading" => "1. Update package",
            "package-help" => "Select the .clonardc-update file supplied with the new version.",
            "choose" => "Choose file",
            "access-heading" => "2. Publishing key",
            "access-help" => "Use a fine-grained GitHub token restricted to XlionHz/clonar-dc with Contents: Read and write. The key never goes inside the package.",
            "remember" => "Store this key encrypted for this Windows user",
            "security" => "Publishing creates a GitHub Release. The app verifies the package SHA-256 before upload; all installed clients then discover the release through the existing updater.",
            "close" => "Close",
            "publish" => "Publish update to everyone",
            "choose-dialog" => "Choose the Clonar DC update package",
            "checking" => "Verifying package…",
            "version" => "Version",
            "size" => "Size",
            "verified" => "Package verified. Review the details before publishing.",
            "invalid" => "The package could not be validated.",
            "key-required" => "Enter the GitHub publishing key.",
            "confirm-title" => "Publish update",
            "confirm-body" => "Version {version} will be published and made available to every Clonar DC installation. Continue?",
            "starting" => "Preparing publication…",
            "published" => "Update published successfully. Installed apps will discover it automatically.",
            "published-title" => "Update published",
            "published-body" => "The update is now available. Open the GitHub release page?",
            "publish-failed" => "The update could not be published.",
            "publishing-active" => "Publishing is still in progress. Wait for it to finish before closing this window.",
            _ => key
        };
    }
}