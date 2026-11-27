using System.Windows;
using System.Windows.Input;
using WindowScreenTime.Models;

namespace WindowScreenTime.Views;

/// <summary>
/// Geri yükleme onayı: yedeğin bilgisi, kaç gün ekleneceği / atlanacağı ve "Ayarları da yedekten al"
/// seçeneği. "Devam" ile kapanırsa DialogResult = true olur.
/// </summary>
public partial class RestoreConfirmWindow : Window
{
    public bool IncludeSettings => IncludeSettingsCheckBox.IsChecked == true;

    public RestoreConfirmWindow(BackupData backup, RestorePreview preview)
    {
        InitializeComponent();

        string machine = string.IsNullOrWhiteSpace(backup.MachineName) ? "" : $"  •  Bilgisayar: {backup.MachineName}";
        string version = string.IsNullOrWhiteSpace(backup.AppVersion) ? "" : $"  •  Sürüm {backup.AppVersion}";
        BackupInfoText.Text =
            $"Yedek tarihi: {backup.CreatedAt:dd.MM.yyyy HH:mm}{machine}{version}\n" +
            $"Yedekteki gün sayısı: {backup.Usage?.Count ?? 0}";

        DaysToAddText.Text = preview.DaysToAdd > 0
            ? $"{preview.DaysToAdd} gün geçmişe eklenecek."
            : "Eklenecek yeni gün yok (yedekteki tüm günler zaten mevcut).";
        SkipText.Text = preview.DaysSkipped > 0
            ? $"{preview.DaysSkipped} gün atlanacak (zaten kaydı var veya bugüne ait)."
            : "Atlanacak gün yok.";

        if (backup.Settings is { } s)
        {
            SettingsInfoText.Text =
                "İşaretlenirse yedekteki ayarlar kullanılır: " + SettingsWindow.DescribeSettings(s) +
                ".\nİşaretlenmezse mevcut ayarlarınız korunur.";
        }
        else
        {
            IncludeSettingsCheckBox.IsEnabled = false;
            SettingsInfoText.Text = "Bu yedekte ayar bilgisi yok; mevcut ayarlarınız korunur.";
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
