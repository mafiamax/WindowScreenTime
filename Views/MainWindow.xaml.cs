using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowScreenTime.Models;
using WindowScreenTime.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace WindowScreenTime.Views;

public partial class MainWindow : Window
{
    // Grafikte en yüksek çubuğun piksel yüksekliği
    private const double MaxBarHeight = 112;

    private readonly DataStorageService _storage;
    private readonly UsageTrackerService _tracker;
    private int _selectedChartDays = 7;
    private bool _isLoadingSettings = true;
    private bool _hasShownTrayBalloon;

    public MainWindow(DataStorageService storage, UsageTrackerService tracker)
    {
        InitializeComponent();

        _storage = storage;
        _tracker = tracker;

        LoadSettingsIntoUi();
        RefreshDashboard();

        _tracker.StatsUpdated += OnTrackerStatsUpdated;
        ThemeService.ThemeChanged += OnThemeChanged;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }

    private void LoadSettingsIntoUi()
    {
        _isLoadingSettings = true;

        StartupCheckBox.IsChecked = _storage.Settings.RunOnStartup;

        SelectComboByTag(IdleTimeoutComboBox,
            _storage.Settings.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture), fallbackIndex: 2);
        SelectComboByTag(BreakReminderComboBox,
            _storage.Settings.BreakReminderMinutes.ToString(CultureInfo.InvariantCulture), fallbackIndex: 3);
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

    private void OnTrackerStatsUpdated()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible)
            {
                RefreshDashboard();
            }
        });
    }

    private void OnThemeChanged()
    {
        // Koddan hesaplanan fırçalar (grafikteki bugün çubuğu, etiketler) yeni temaya göre yenilenir
        RefreshDashboard();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _tracker.NotifyUserPresent();
        RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        DailyUsage today = _tracker.GetTodayUsage();

        // 1. Durum rozeti (SetResourceReference: tema değişince renkler kendiliğinden güncellenir)
        if (_tracker.IsLockedOrSleeping)
        {
            SetStatusBadge(warn: true, "Kilitli / Uykuda (Duraklatıldı)");
        }
        else if (_tracker.IsCurrentlyIdle)
        {
            SetStatusBadge(warn: true, "Boşta (Idle Modu)");
        }
        else
        {
            SetStatusBadge(warn: false, "Aktif Takip Ediliyor");
        }

        // 2. Üst özet kartları
        TodayActiveText.Text = today.ActiveFormatted;
        TodayIdleSubtext.Text = $"Boşta: {today.IdleFormatted}  •  Uykuda: {today.SleepFormatted}";

        string avg7 = _tracker.GetAverageActiveFormatted(7);
        string avg30 = _tracker.GetAverageActiveFormatted(30);
        Avg7DaysText.Text = avg7;
        Avg30DaysSubtext.Text = $"Son 7 gün ortalaması  •  30 Gün: {avg30}";

        TodayFirstSeenText.Text = today.FirstSeenFormatted;
        TodayLastSeenText.Text = today.LastSeenFormatted;
        TodayDateSubtext.Text = today.DisplayDate;

        // 3. Grafik güncelleme
        UpdateChartSection();

        // 4. Tablo güncelleme
        var tableItems = _storage.GetRecentDays(30)
            .Where(d => d.ActiveSeconds > 0 || d.IdleSeconds > 0 || d.SleepSeconds > 0 || d.DateKey == today.DateKey)
            .ToList();

        HistoryDataGrid.ItemsSource = null;
        HistoryDataGrid.ItemsSource = tableItems;
    }

    private void SetStatusBadge(bool warn, string text)
    {
        string prefix = warn ? "StatusWarn" : "StatusActive";
        StatusBadgeBorder.SetResourceReference(Border.BackgroundProperty, prefix + "BgBrush");
        StatusBadgeBorder.SetResourceReference(Border.BorderBrushProperty, prefix + "BorderBrush");
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, prefix + "DotBrush");
        StatusBadgeText.SetResourceReference(TextBlock.ForegroundProperty, prefix + "TextBrush");
        StatusBadgeText.Text = text;
    }

    private Brush ThemeBrush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;

    private void UpdateChartSection()
    {
        HighlightSelectedRangeButton();

        // Eskiden yeniye doğru sırala (grafikte soldan sağa)
        var days = _storage.GetRecentDays(_selectedChartDays)
            .OrderBy(d => d.DateKey)
            .ToList();

        // Ölçek: gösterilen günlerdeki en uzun "açık" süre (Aktif + Boşta + Uyku), en az 1 saat
        long maxTotal = days.Count > 0 ? days.Max(d => d.ActiveSeconds + d.IdleSeconds + d.SleepSeconds) : 0;
        if (maxTotal < 3600)
            maxTotal = 3600;

        long peakActive = days.Count > 0 ? days.Max(d => d.ActiveSeconds) : 0;
        ChartMaxInfoText.Text = $"(En yüksek aktif: {DailyUsage.FormatDuration(peakActive)})";

        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        Brush activeBrush = ThemeBrush("ChartActiveBrush");
        Brush todayActiveBrush = new LinearGradientBrush(Color.FromRgb(56, 189, 248), Color.FromRgb(99, 102, 241), 90);
        Brush labelBrush = ThemeBrush("ChartLabelBrush");
        Brush todayLabelBrush = ThemeBrush("ChartTodayLabelBrush");

        var chartItems = new List<ChartBarViewModel>(days.Count);

        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            bool isToday = d.DateKey == todayKey;

            string dayLabel = _selectedChartDays <= 14
                ? d.ShortDayLabel
                : (i % 2 == 0 || isToday ? d.ParsedDate.ToString("dd/MM") : "");

            string topLabel = _selectedChartDays <= 14 && d.ActiveSeconds > 0
                ? $"{d.ActiveHours:0.0}s"
                : "";

            var vm = ChartBarViewModel.Create(d.ActiveSeconds, d.IdleSeconds, d.SleepSeconds, maxTotal, MaxBarHeight);
            vm.DayLabel = dayLabel;
            vm.TopValueLabel = topLabel;
            vm.ActiveBrush = isToday ? todayActiveBrush : activeBrush;
            vm.LabelBrush = isToday ? todayLabelBrush : labelBrush;
            vm.LabelWeight = isToday ? FontWeights.Bold : FontWeights.Normal;
            vm.TooltipText = $"{d.DisplayDate}\nAktif: {d.ActiveFormatted}\nBoşta: {d.IdleFormatted}\nUykuda: {d.SleepFormatted}\nİlk Açılış: {d.FirstSeenFormatted} - Son: {d.LastSeenFormatted}";
            chartItems.Add(vm);
        }

        ChartItemsControl.ItemsSource = chartItems;
    }

    private void HighlightSelectedRangeButton()
    {
        var normal = (Style)FindResource("ModernButtonStyle");
        var selected = (Style)FindResource("SelectedButtonStyle");

        Range7Btn.Style = _selectedChartDays == 7 ? selected : normal;
        Range14Btn.Style = _selectedChartDays == 14 ? selected : normal;
        Range30Btn.Style = _selectedChartDays == 30 ? selected : normal;
    }

    private void Range7Btn_Click(object sender, RoutedEventArgs e)
    {
        _selectedChartDays = 7;
        UpdateChartSection();
    }

    private void Range14Btn_Click(object sender, RoutedEventArgs e)
    {
        _selectedChartDays = 14;
        UpdateChartSection();
    }

    private void Range30Btn_Click(object sender, RoutedEventArgs e)
    {
        _selectedChartDays = 30;
        UpdateChartSection();
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;

        _storage.Settings.RunOnStartup = StartupCheckBox.IsChecked == true;
        _storage.SaveSettings();
    }

    private void IdleTimeoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;

        if (IdleTimeoutComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int minutes))
        {
            _storage.Settings.IdleTimeoutMinutes = minutes;
            _storage.SaveSettings();
            _tracker.ProcessElapsedUntilNow();
        }
    }

    private void BreakReminderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;

        if (BreakReminderComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int minutes))
        {
            _storage.Settings.BreakReminderMinutes = minutes;
            _storage.SaveSettings();
        }
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
        }
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        _tracker.ProcessElapsedUntilNow();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Kullanım geçmişini dışa aktar",
            Filter = "CSV dosyası (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"WindowScreenTime_Gecmis_{DateTime.Today:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var days = _storage.GetAllRecordedDays();
            CsvExportService.WriteCsv(dialog.FileName, days);
            System.Windows.MessageBox.Show(
                $"{days.Count} günlük kayıt dışa aktarıldı:\n{dialog.FileName}",
                "Dışa Aktarma Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Dosya kaydedilemedi:\n{ex.Message}\n\nDosya Excel'de açıksa kapatıp tekrar deneyin.",
                "Dışa Aktarma Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ResetHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            "Bugün dahil kayıtlı tüm günler silinecek ve sayaçlar şu andan itibaren sıfırdan başlayacak.\n\n" +
            "• Silinen geçmiş, veri klasörüne yedek dosyası olarak kaydedilir.\n" +
            "• Ayarlarınız (tema, boşta sayma, mola hatırlatma, Windows ile başlat) değişmez.\n" +
            "• İsterseniz geçmiş günleri daha sonra \"Olay Günlüğünü Tara\" ile yeniden doldurabilirsiniz.\n\n" +
            "Devam etmek istiyor musunuz?",
            "Geçmişi Sıfırla",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            string backupPath = _tracker.ResetHistory();
            RefreshDashboard();
            System.Windows.MessageBox.Show(
                "Kullanım geçmişi sıfırlandı; sayaçlar şu andan itibaren yeniden başladı.\n\n" +
                $"Eski geçmişin yedeği:\n{backupPath}\n\n" +
                "(Veri klasörüne Hakkında penceresindeki \"Veri Klasörünü Aç\" ile ulaşabilirsiniz.)",
                "Sıfırlama Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Geçmiş sıfırlanamadı; hiçbir kayıt silinmedi.\n\n{ex.Message}",
                "Sıfırlama Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ShowAboutWindow();
        }
    }

    private async void ImportEventLogButton_Click(object sender, RoutedEventArgs e)
    {
        ImportEventLogButton.IsEnabled = false;
        ImportEventLogButton.Content = "Taranıyor...";

        try
        {
            int count = await _tracker.ImportEventLogHistoryAsync();
            RefreshDashboard();
            System.Windows.MessageBox.Show(
                $"Windows Olay Günlüğü tarandı ve {count} günlük geçmiş oturum kaydı güncellendi.",
                "Geçmiş Aktarıldı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            ImportEventLogButton.Content = "Olay Günlüğünü Tara";
            ImportEventLogButton.IsEnabled = true;
        }
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        HideToSystemTray();
    }

    private void ExitAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            HideToSystemTray();
        }
    }

    private void HideToSystemTray()
    {
        Hide();
        if (!_hasShownTrayBalloon && System.Windows.Application.Current is App app)
        {
            _hasShownTrayBalloon = true;
            app.ShowMinimizedNotificationOnce();
        }
    }
}

/// <summary>
/// Grafikteki tek bir günün yığılı çubuğu: alttan üste Aktif, Boşta, Uyku.
/// </summary>
public class ChartBarViewModel
{
    private const double SegmentRadius = 4;
    private const double MinVisibleSegment = 2;
    private const double EmptyBarHeight = 4;

    public string DayLabel { get; set; } = "";
    public string TopValueLabel { get; set; } = "";
    public double ActiveHeight { get; set; }
    public double IdleHeight { get; set; }
    public double SleepHeight { get; set; }
    public double EmptyHeight { get; set; }
    public CornerRadius ActiveCorner { get; set; }
    public CornerRadius IdleCorner { get; set; }
    public CornerRadius SleepCorner { get; set; }
    public Brush ActiveBrush { get; set; } = Brushes.DodgerBlue;
    public Brush LabelBrush { get; set; } = Brushes.Gray;
    public FontWeight LabelWeight { get; set; } = FontWeights.Normal;
    public string TooltipText { get; set; } = "";

    /// <summary>
    /// Süreleri piksel yüksekliklerine çevirir. Sıfırdan büyük ama çok küçük dilimler en az birkaç piksel
    /// gösterilir. En üstteki dilimin üst köşeleri, en alttakinin alt köşeleri yuvarlatılır.
    /// </summary>
    public static ChartBarViewModel Create(long activeSec, long idleSec, long sleepSec, long scaleSec, double maxHeight)
    {
        var vm = new ChartBarViewModel();
        double Scale(long s) => s <= 0 || scaleSec <= 0 ? 0 : Math.Max(MinVisibleSegment, Math.Round(s / (double)scaleSec * maxHeight));

        vm.ActiveHeight = Scale(activeSec);
        vm.IdleHeight = Scale(idleSec);
        vm.SleepHeight = Scale(sleepSec);

        // Yuvarlamalar toplamı taşırırsa en büyük dilimden kırp
        double total = vm.ActiveHeight + vm.IdleHeight + vm.SleepHeight;
        if (total > maxHeight)
        {
            double excess = total - maxHeight;
            if (vm.SleepHeight >= vm.IdleHeight && vm.SleepHeight >= vm.ActiveHeight) vm.SleepHeight -= excess;
            else if (vm.IdleHeight >= vm.ActiveHeight) vm.IdleHeight -= excess;
            else vm.ActiveHeight -= excess;
        }

        if (vm.ActiveHeight + vm.IdleHeight + vm.SleepHeight <= 0)
        {
            vm.EmptyHeight = EmptyBarHeight;
            return vm;
        }

        // Görünür dilimler yukarıdan aşağıya: Uyku, Boşta, Aktif
        var visible = new List<string>();
        if (vm.SleepHeight > 0) visible.Add("sleep");
        if (vm.IdleHeight > 0) visible.Add("idle");
        if (vm.ActiveHeight > 0) visible.Add("active");

        CornerRadius CornerFor(string name)
        {
            bool isTop = visible[0] == name;
            bool isBottom = visible[^1] == name;
            double top = isTop ? SegmentRadius : 0;
            double bottom = isBottom ? SegmentRadius : 0;
            return new CornerRadius(top, top, bottom, bottom);
        }

        if (vm.SleepHeight > 0) vm.SleepCorner = CornerFor("sleep");
        if (vm.IdleHeight > 0) vm.IdleCorner = CornerFor("idle");
        if (vm.ActiveHeight > 0) vm.ActiveCorner = CornerFor("active");

        return vm;
    }
}
