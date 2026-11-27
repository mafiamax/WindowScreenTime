using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowScreenTime.Models;
using WindowScreenTime.Services;

namespace WindowScreenTime.Views;

/// <summary>
/// Ayarlar penceresi: genel ayarlar (seçildiği anda uygulanıp kaydedilir) ve Verileri Yedekle /
/// Geri Yükle. Ayar kayıt yapısı (<see cref="AppSettings"/>) ana penceredeki eski ayar çubuğuyla aynıdır.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly DataStorageService _storage;
    private readonly UsageTrackerService _tracker;
    private readonly Action _onSettingsChanged;
    private bool _isLoadingSettings = true;

    public SettingsWindow(DataStorageService storage, UsageTrackerService tracker, Action onSettingsChanged)
    {
        InitializeComponent();

        _storage = storage;
        _tracker = tracker;
        _onSettingsChanged = onSettingsChanged;

        LoadSettingsIntoUi();
    }

    /// <summary>Ayarların kısa, okunur özeti (ana penceredeki alt çubuk ve geri yükleme onayında kullanılır).</summary>
    public static string DescribeSettings(AppSettings s)
    {
        string theme = ThemeService.Normalize(s.Theme) switch
        {
            "Light" => "Açık",
            "System" => "Sistem",
            _ => "Koyu"
        };
        string breakText = s.BreakReminderMinutes > 0 ? $"{s.BreakReminderMinutes} dk" : "Kapalı";
        string goalText = s.DailyGoalMinutes <= 0 ? "Kapalı"
            : s.DailyGoalMinutes % 60 == 0 ? $"{s.DailyGoalMinutes / 60} saat"
            : DailyUsage.FormatDuration(s.DailyGoalMinutes * 60L);
        return $"Boşta sayma: {s.IdleTimeoutMinutes} dk  •  Mola: {breakText}  •  Hedef: {goalText}  •  " +
               $"Tema: {theme}  •  Windows ile başlat: {(s.RunOnStartup ? "Açık" : "Kapalı")}";
    }

    private void LoadSettingsIntoUi()
    {
        _isLoadingSettings = true;

        StartupCheckBox.IsChecked = _storage.Settings.RunOnStartup;
        SelectComboByTag(IdleTimeoutComboBox,
            _storage.Settings.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture), fallbackIndex: 2);
        SelectComboByTag(BreakReminderComboBox,
            _storage.Settings.BreakReminderMinutes.ToString(CultureInfo.InvariantCulture), fallbackIndex: 3);
        SelectComboByTag(DailyGoalComboBox,
            _storage.Settings.DailyGoalMinutes.ToString(CultureInfo.InvariantCulture), fallbackIndex: 0);
        SelectComboByTag(ThemeComboBox, ThemeService.Normalize(_storage.Settings.Theme), fallbackIndex: 0);

        _isLoadingSettings = false;
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag, int fallbackIndex)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = fallbackIndex;
    }

    private static bool TryGetTagMinutes(System.Windows.Controls.ComboBox combo, out int minutes)
    {
        minutes = 0;
        return combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out minutes);
    }

    // ---------------- Genel ayarlar ----------------

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;

        _storage.Settings.RunOnStartup = StartupCheckBox.IsChecked == true;
        _storage.SaveSettings();
        _onSettingsChanged();
    }

    private void IdleTimeoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || !TryGetTagMinutes(IdleTimeoutComboBox, out int minutes)) return;

        _storage.Settings.IdleTimeoutMinutes = minutes;
        _storage.SaveSettings();
        _tracker.ProcessElapsedUntilNow();
        _onSettingsChanged();
    }

    private void BreakReminderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || !TryGetTagMinutes(BreakReminderComboBox, out int minutes)) return;

        _storage.Settings.BreakReminderMinutes = minutes;
        _storage.SaveSettings();
        _onSettingsChanged();
    }

    private void DailyGoalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || !TryGetTagMinutes(DailyGoalComboBox, out int minutes)) return;

        _storage.Settings.DailyGoalMinutes = minutes;
        // Yeni hedef için bugün yeniden bildirim gösterilebilsin
        _storage.Settings.DailyGoalNotifiedDate = null;
        _storage.SaveSettings();
        _onSettingsChanged();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            string theme = ThemeService.Normalize(item.Tag?.ToString());
            _storage.Settings.Theme = theme;
            _storage.SaveSettings();
            ThemeService.Apply(theme);
            _onSettingsChanged();
        }
    }

    // ---------------- Verileri Yedekle / Geri Yükle ----------------

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Verileri yedekle",
            Filter = "WindowScreenTime yedeği (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = $"WindowScreenTime-yedek-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            int days = _tracker.ExportBackup(dialog.FileName);
            System.Windows.MessageBox.Show(this,
                $"Kullanım geçmişi ({days} gün) ve ayarlar yedeklendi:\n{dialog.FileName}\n\n" +
                "Bu dosyayı \"Verileri Geri Yükle\" ile bu veya başka bir bilgisayarda geri yükleyebilirsiniz.",
                "Yedekleme Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"Yedek dosyası kaydedilemedi:\n{ex.Message}",
                "Yedekleme Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Geri yüklenecek yedeği seçin",
            Filter = "WindowScreenTime yedeği (*.json)|*.json|Tüm dosyalar (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        // 1) Oku ve doğrula: geçersizse hiçbir şey değiştirilmez
        BackupData backup;
        try
        {
            backup = DataStorageService.ReadBackup(dialog.FileName);
        }
        catch (Exception ex)
        {
            string message = ex is InvalidDataException
                ? ex.Message
                : $"Yedek dosyası okunamadı:\n{ex.Message}";
            System.Windows.MessageBox.Show(this,
                message + "\n\nHiçbir veri değiştirilmedi.",
                "Geri Yükleme",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 2) Onay: kaç gün eklenecek / atlanacak, ayarlar alınsın mı?
        var confirm = new RestoreConfirmWindow(backup, _storage.PreviewRestore(backup)) { Owner = this };
        if (confirm.ShowDialog() != true)
            return;

        // 3) Geri yükle (önce mevcut veriler veri klasörüne yedeklenir)
        RestoreResult result;
        try
        {
            result = _tracker.RestoreBackup(backup, confirm.IncludeSettings);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"Geri yükleme yapılamadı; hiçbir veri değiştirilmedi.\n\n{ex.Message}",
                "Geri Yükleme Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (result.SettingsRestored)
        {
            LoadSettingsIntoUi();
            ThemeService.Apply(_storage.Settings.Theme);
        }
        _onSettingsChanged();

        System.Windows.MessageBox.Show(this,
            $"Geri yükleme tamamlandı.\n\n" +
            $"• Eklenen gün: {result.DaysAdded}\n" +
            $"• Atlanan gün (zaten mevcut): {result.DaysSkipped}\n" +
            $"• Bugün: {(result.TodayUpdated ? "yedekteki daha uzun sürelerle güncellendi" : "değiştirilmedi")}\n" +
            $"• Ayarlar: {(result.SettingsRestored ? "yedekten alındı" : "değiştirilmedi")}\n\n" +
            $"İşlemden önceki verilerinizin yedeği:\n{result.SafetyBackupPath}",
            "Geri Yükleme Tamamlandı",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ---------------- Pencere ----------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
