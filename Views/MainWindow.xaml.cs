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
    }

    private void LoadSettingsIntoUi()
    {
        _isLoadingSettings = true;

        StartupCheckBox.IsChecked = _storage.Settings.RunOnStartup;

        string targetTag = _storage.Settings.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);
        bool matched = false;
        foreach (ComboBoxItem item in IdleTimeoutComboBox.Items)
        {
            if (item.Tag?.ToString() == targetTag)
            {
                IdleTimeoutComboBox.SelectedItem = item;
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            IdleTimeoutComboBox.SelectedIndex = 2; // 5 dakika
        }

        _isLoadingSettings = false;
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

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _tracker.NotifyUserPresent();
        RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        DailyUsage today = _tracker.GetTodayUsage();

        // 1. Durum rozeti
        if (_tracker.IsLockedOrSleeping)
        {
            StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(55, 48, 28));
            StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(253, 230, 138));
            StatusBadgeText.Text = "Kilitli / Uykuda (Duraklatıldı)";
        }
        else if (_tracker.IsCurrentlyIdle)
        {
            StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(55, 48, 28));
            StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(253, 230, 138));
            StatusBadgeText.Text = "Boşta (Idle Modu)";
        }
        else
        {
            StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(20, 51, 43));
            StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(110, 231, 183));
            StatusBadgeText.Text = "Aktif Takip Ediliyor";
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

    private void UpdateChartSection()
    {
        HighlightSelectedRangeButton();

        // Eskiden yeniye doğru sırala (grafikte soldan sağa)
        var days = _storage.GetRecentDays(_selectedChartDays)
            .OrderBy(d => d.DateKey)
            .ToList();

        long maxSeconds = days.Count > 0 ? days.Max(d => d.ActiveSeconds) : 0;
        if (maxSeconds < 3600)
            maxSeconds = 3600; // Minimum 1 saatlik ölçek

        ChartMaxInfoText.Text = $"(Tepe: {DailyUsage.FormatDuration(days.Max(d => d.ActiveSeconds))})";

        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        var chartItems = new List<ChartBarViewModel>(days.Count);

        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            bool isToday = d.DateKey == todayKey;
            double ratio = maxSeconds > 0 ? (double)d.ActiveSeconds / maxSeconds : 0;
            double barHeight = Math.Max(4, Math.Round(ratio * 108));

            string dayLabel = _selectedChartDays <= 14
                ? d.ShortDayLabel
                : (i % 2 == 0 || isToday ? d.ParsedDate.ToString("dd/MM") : "");

            string topLabel = _selectedChartDays <= 14 && d.ActiveSeconds > 0
                ? $"{d.ActiveHours:0.0}s"
                : "";

            Brush barBrush = isToday
                ? new LinearGradientBrush(Color.FromRgb(56, 189, 248), Color.FromRgb(99, 102, 241), 90)
                : new SolidColorBrush(Color.FromRgb(59, 130, 246));

            if (d.ActiveSeconds == 0)
            {
                barBrush = new SolidColorBrush(Color.FromRgb(38, 43, 54));
            }

            chartItems.Add(new ChartBarViewModel
            {
                DayLabel = dayLabel,
                TopValueLabel = topLabel,
                BarHeight = barHeight,
                BarBrush = barBrush,
                LabelBrush = isToday
                    ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                    : new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                LabelWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                TooltipText = $"{d.DisplayDate}\nAktif: {d.ActiveFormatted}\nBoşta: {d.IdleFormatted}\nUykuda: {d.SleepFormatted}\nİlk Açılış: {d.FirstSeenFormatted} - Son: {d.LastSeenFormatted}"
            });
        }

        ChartItemsControl.ItemsSource = chartItems;
    }

    private void HighlightSelectedRangeButton()
    {
        var activeBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        var inactiveBrush = new SolidColorBrush(Color.FromRgb(38, 43, 54));

        Range7Btn.Background = _selectedChartDays == 7 ? activeBrush : inactiveBrush;
        Range14Btn.Background = _selectedChartDays == 14 ? activeBrush : inactiveBrush;
        Range30Btn.Background = _selectedChartDays == 30 ? activeBrush : inactiveBrush;
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

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Kontrol Ediliyor...";

        try
        {
            await UpdateService.CheckForUpdatesAsync(isManualCheck: true);
        }
        finally
        {
            CheckUpdateButton.Content = "Güncellemeleri Kontrol Et";
            CheckUpdateButton.IsEnabled = true;
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

public class ChartBarViewModel
{
    public string DayLabel { get; set; } = "";
    public string TopValueLabel { get; set; } = "";
    public double BarHeight { get; set; }
    public Brush BarBrush { get; set; } = Brushes.DodgerBlue;
    public Brush LabelBrush { get; set; } = Brushes.Gray;
    public FontWeight LabelWeight { get; set; } = FontWeights.Normal;
    public string TooltipText { get; set; } = "";
}
