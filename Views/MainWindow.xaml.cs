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
    private static readonly CultureInfo TrCulture = new("tr-TR");

    private int _selectedChartDays = 7;
    // Tabloda gösterilen dönem: null = son 30 gün, "yyyy-MM" = o ay
    private string? _selectedHistoryMonth;
    private List<string>? _historyMonthKeys;
    private bool _isUpdatingHistoryRange;
    private bool _hasShownTrayBalloon;

    public MainWindow(DataStorageService storage, UsageTrackerService tracker)
    {
        InitializeComponent();

        _storage = storage;
        _tracker = tracker;

        RefreshDashboard();

        _tracker.StatsUpdated += OnTrackerStatsUpdated;
        ThemeService.ThemeChanged += OnThemeChanged;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
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
        int goalMinutes = _storage.Settings.DailyGoalMinutes;
        string goalPart = goalMinutes > 0
            ? $"  •  Hedef: %{Math.Round(today.ActiveSeconds * 100.0 / (goalMinutes * 60.0)):0}"
            : "";
        TodayIdleSubtext.Text = $"Boşta: {today.IdleFormatted}  •  Uykuda: {today.SleepFormatted}{goalPart}";

        string avg7 = _tracker.GetAverageActiveFormatted(7);
        string avg30 = _tracker.GetAverageActiveFormatted(30);
        Avg7DaysText.Text = avg7;
        Avg30DaysSubtext.Text = $"Son 7 gün ortalaması  •  30 Gün: {avg30}";

        TodayFirstSeenText.Text = today.FirstSeenFormatted;
        TodayLastSeenText.Text = today.LastSeenFormatted;
        TodayDateSubtext.Text = today.DisplayDate;

        // 3. Grafik güncelleme
        UpdateChartSection();

        // 4. Tablo güncelleme (son 30 gün veya seçilen ay)
        UpdateHistoryRangeOptions(today.DateKey);

        List<DailyUsage> tableItems;
        string summaryLabel;
        if (_selectedHistoryMonth is { } month)
        {
            tableItems = _storage.GetAllRecordedDays()
                .Where(d => d.DateKey.StartsWith(month + "-", StringComparison.Ordinal))
                .Where(d => HasAnyTime(d) || d.DateKey == today.DateKey)
                .ToList();
            summaryLabel = "Ay toplamı";
        }
        else
        {
            tableItems = _storage.GetRecentDays(30)
                .Where(d => HasAnyTime(d) || d.DateKey == today.DateKey)
                .ToList();
            summaryLabel = "30 gün toplamı";
        }

        UpdateHistoryTable(tableItems);
        UpdateHistorySummary(summaryLabel, tableItems);

        // 5. Alt çubuk: geçerli ayarların özeti
        SettingsSummaryText.Text = SettingsWindow.DescribeSettings(_storage.Settings);
    }

    private static bool HasAnyTime(DailyUsage d) => d.ActiveSeconds > 0 || d.IdleSeconds > 0 || d.SleepSeconds > 0;

    /// <summary>
    /// Dönem seçicisini doldurur: "Son 30 gün" + kaydı olan her ay (yeniden eskiye). Liste yalnızca
    /// ay kümesi değiştiğinde yeniden kurulur; seçili dönem korunur. Seçili ayın kaydı kalmadıysa
    /// (ör. geçmiş sıfırlandı) "Son 30 gün"e dönülür.
    /// </summary>
    private void UpdateHistoryRangeOptions(string todayKey)
    {
        var months = _storage.GetAllRecordedDays()
            .Where(d => HasAnyTime(d) || d.DateKey == todayKey)
            .Select(d => d.DateKey.Length >= 7 ? d.DateKey[..7] : d.DateKey)
            .Distinct()
            .OrderByDescending(m => m, StringComparer.Ordinal)
            .ToList();

        if (_historyMonthKeys != null && _historyMonthKeys.SequenceEqual(months))
            return;

        _historyMonthKeys = months;
        if (_selectedHistoryMonth != null && !months.Contains(_selectedHistoryMonth))
            _selectedHistoryMonth = null;

        _isUpdatingHistoryRange = true;
        try
        {
            HistoryRangeComboBox.Items.Clear();
            HistoryRangeComboBox.Items.Add(new ComboBoxItem { Content = "Son 30 gün", Tag = "" });
            foreach (string m in months)
            {
                string label = DateTime.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt)
                    ? dt.ToString("MMMM yyyy", TrCulture)
                    : m;
                HistoryRangeComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = m });
            }
            SelectComboByTag(HistoryRangeComboBox, _selectedHistoryMonth ?? "", fallbackIndex: 0);
        }
        finally
        {
            _isUpdatingHistoryRange = false;
        }
    }

    private void UpdateHistorySummary(string label, List<DailyUsage> items)
    {
        long totalActive = items.Sum(d => d.ActiveSeconds);
        var activeDays = items.Where(d => d.ActiveSeconds > 0).ToList();
        string avg = activeDays.Count > 0
            ? DailyUsage.FormatDuration((long)activeDays.Average(d => d.ActiveSeconds))
            : "0 dk";
        HistorySummaryText.Text =
            $"{label}: {DailyUsage.FormatDuration(totalActive)} aktif  •  Günlük ort.: {avg}  •  {activeDays.Count} gün";
    }

    private void HistoryRangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingHistoryRange) return;

        string? tag = (HistoryRangeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _selectedHistoryMonth = string.IsNullOrEmpty(tag) ? null : tag;
        RefreshDashboard();
    }

    /// <summary>
    /// Satırlar aynıysa (aynı günler, aynı sırada) yalnızca hücre değerlerini yeniler; tablo baştan
    /// yüklenmez, böylece kullanıcı aşağı kaydırmışsa en üste atlamaz ve seçili satır kaybolmaz.
    /// Satırlar değiştiyse (yeni gün, sıfırlama, yedekten geri yükleme) tablo yeniden yüklenir; bu
    /// durumda da seçili gün korunur.
    /// </summary>
    private void UpdateHistoryTable(List<DailyUsage> items)
    {
        if (HistoryDataGrid.ItemsSource is List<DailyUsage> current &&
            current.Count == items.Count &&
            current.Zip(items).All(p => ReferenceEquals(p.First, p.Second)))
        {
            foreach (var item in current)
                item.NotifyAllChanged();
            return;
        }

        string? selectedKey = (HistoryDataGrid.SelectedItem as DailyUsage)?.DateKey;
        HistoryDataGrid.ItemsSource = items;
        if (selectedKey != null)
        {
            HistoryDataGrid.SelectedItem = items.FirstOrDefault(d => d.DateKey == selectedKey);
        }
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

        // Ölçek: gösterilen günlerdeki en uzun "açık" süre (Aktif + Boşta + Uyku), en az 1 saat.
        // Günlük hedef açıksa hedef çizgisi her zaman görünsün diye ölçek en az hedef kadardır.
        long goalSeconds = Math.Max(0, _storage.Settings.DailyGoalMinutes) * 60L;
        long maxTotal = days.Count > 0 ? days.Max(d => d.ActiveSeconds + d.IdleSeconds + d.SleepSeconds) : 0;
        if (maxTotal < 3600)
            maxTotal = 3600;
        if (maxTotal < goalSeconds)
            maxTotal = goalSeconds;

        double goalOffset = goalSeconds > 0 ? Math.Round(goalSeconds / (double)maxTotal * MaxBarHeight) : 0;
        var goalMargin = new Thickness(0, 0, 0, Math.Max(0, goalOffset - 1));
        var goalVisibility = goalSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        GoalLegend.Visibility = goalVisibility;
        GoalLegendText.Text = $"Hedef ({DailyUsage.FormatDuration(goalSeconds)})";

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
            vm.GoalMargin = goalMargin;
            vm.GoalVisibility = goalVisibility;
            vm.TooltipText = $"{d.DisplayDate}\nAktif: {d.ActiveFormatted}\nBoşta: {d.IdleFormatted}\nUykuda: {d.SleepFormatted}\nİlk Açılış: {d.FirstSeenFormatted} - Son: {d.LastSeenFormatted}"
                + (goalSeconds > 0 ? $"\nHedef: {DailyUsage.FormatDuration(goalSeconds)}" : "");
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void SettingsSummaryText_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSettings();

    private static void OpenSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ShowSettingsWindow();
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
            "• Silmeden önce geçmiş ve ayarlar veri klasörüne yedeklenir; istediğinizde Ayarlar → \"Verileri Geri Yükle\" ile geri alabilirsiniz.\n" +
            "• Ayarlarınız (tema, boşta sayma, mola hatırlatma, günlük hedef, Windows ile başlat) değişmez.\n\n" +
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
                $"Sıfırlamadan önceki verilerin yedeği:\n{backupPath}\n\n" +
                "Geri almak için: Ayarlar → \"Verileri Geri Yükle\" → bu dosyayı seçin.\n" +
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
    public Thickness GoalMargin { get; set; }
    public Visibility GoalVisibility { get; set; } = Visibility.Collapsed;

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
