using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WindowScreenTime.Services;
using WindowScreenTime.Views;
using Forms = System.Windows.Forms;

namespace WindowScreenTime;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Global\WindowScreenTime_SingleInstance_Mutex";
    private const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowScreenTime";

    private Mutex? _instanceMutex;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _todayStatusMenuItem;
    private Icon? _trayIconHandle;

    public DataStorageService Storage { get; private set; } = null!;
    public UsageTrackerService Tracker { get; private set; } = null!;
    public bool IsExiting { get; private set; }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstaller();
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "WindowScreenTime zaten arka planda (Sistem Tepsisinde) çalışıyor.",
                "WindowScreenTime",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Storage = new DataStorageService();
        ThemeService.Apply(Storage.Settings.Theme);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Tracker = new UsageTrackerService(Storage);
        Tracker.StatsUpdated += OnTrackerStatsUpdated;
        Tracker.BreakReminderDue += OnBreakReminderDue;

        InitializeTrayIcon();
        Tracker.Start();

        var mainWindow = new MainWindow(Storage, Tracker);
        MainWindow = mainWindow;

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            mainWindow.Show();
        }
    }

    private static void RunUninstaller()
    {
        var confirm = System.Windows.MessageBox.Show(
            "WindowScreenTime uygulamasını bilgisayarınızdan kaldırmak istediğinize emin misiniz?\n\n(Not: Geçmiş kullanım verileriniz silinmez, korunur.)",
            "WindowScreenTime Kaldırma Sihirbazı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            // Açık olan başka WindowScreenTime işlemlerini kapat
            int currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("WindowScreenTime"))
            {
                if (proc.Id != currentPid)
                {
                    try { proc.Kill(); proc.WaitForExit(2000); } catch { }
                }
            }

            // Kısayolları temizle
            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WindowScreenTime.lnk");
            string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WindowScreenTime.lnk");
            string startupCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WindowScreenTime.cmd");

            if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
            if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);
            if (File.Exists(startupCmd)) File.Delete(startupCmd);

            // Program Ekle/Kaldır kaydını sil
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKey, throwOnMissingSubKey: false);

            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WindowScreenTime");

            System.Windows.MessageBox.Show(
                "WindowScreenTime başarıyla kaldırıldı.",
                "WindowScreenTime",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (Directory.Exists(installDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Kaldırma sırasında hata oluştu:\n{ex.Message}",
                "WindowScreenTime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIconHandle = CreateModernTrayIcon();

        var contextMenu = new Forms.ContextMenuStrip();
        _todayStatusMenuItem = new Forms.ToolStripMenuItem("Bugün: 0 dk")
        {
            Enabled = false
        };

        var openItem = new Forms.ToolStripMenuItem("Kontrol Panelini Aç");
        openItem.Click += (_, _) => ShowDashboardWindow();

        var aboutItem = new Forms.ToolStripMenuItem("Hakkında");
        aboutItem.Click += (_, _) => ShowAboutWindow();

        var exitItem = new Forms.ToolStripMenuItem("Tamamen Çıkış Yap");
        exitItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(_todayStatusMenuItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(aboutItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconHandle,
            Visible = true,
            Text = "WindowScreenTime - Bugün: 0 dk",
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowDashboardWindow();
        UpdateTrayTooltip();
    }

    private void OnBreakReminderDue(TimeSpan continuousActive)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string duration = Models.DailyUsage.FormatDuration((long)continuousActive.TotalSeconds);
            _notifyIcon?.ShowBalloonTip(
                8000,
                "Mola zamanı",
                $"{duration} kesintisiz çalıştınız. Kısa bir ara verip gözlerinizi dinlendirin.",
                Forms.ToolTipIcon.Info);
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.InvokeAsync(ThemeService.ReapplyIfFollowingSystem);
        }
    }

    private void OnTrackerStatsUpdated()
    {
        Dispatcher.InvokeAsync(UpdateTrayTooltip);
    }

    private void UpdateTrayTooltip()
    {
        if (_notifyIcon == null) return;

        var today = Tracker.GetTodayUsage();
        string statusText = $"Bugün: {today.ActiveFormatted}";
        string fullTip = $"WindowScreenTime\n{statusText}";

        if (fullTip.Length >= 63)
            fullTip = fullTip[..62];

        _notifyIcon.Text = fullTip;
        if (_todayStatusMenuItem != null)
        {
            _todayStatusMenuItem.Text = $"Bugün Aktif: {today.ActiveFormatted}";
        }
    }

    public void ShowDashboardWindow()
    {
        if (MainWindow == null)
        {
            MainWindow = new MainWindow(Storage, Tracker);
        }

        if (!MainWindow.IsVisible)
        {
            MainWindow.Show();
        }

        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
        MainWindow.Focus();
    }

    private AboutWindow? _aboutWindow;

    /// <summary>
    /// Hakkında penceresini açar (tek örnek). Ana pencere görünürse onun ortasında, değilse
    /// (tepsi menüsünden açıldığında) ekranın ortasında ve en üstte açılır.
    /// </summary>
    public void ShowAboutWindow()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(Storage.DataDirectory);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;

        if (MainWindow != null && MainWindow.IsVisible && MainWindow.WindowState != WindowState.Minimized)
        {
            _aboutWindow.Owner = MainWindow;
            _aboutWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            _aboutWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _aboutWindow.ShowInTaskbar = true;
        }

        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    public void ShowMinimizedNotificationOnce()
    {
        _notifyIcon?.ShowBalloonTip(
            2500,
            "WindowScreenTime",
            "Uygulama arka planda süre takibine devam ediyor.",
            Forms.ToolTipIcon.Info);
    }

    public void ExitApplication()
    {
        IsExiting = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Tracker.BreakReminderDue -= OnBreakReminderDue;
        Tracker.StatsUpdated -= OnTrackerStatsUpdated;
        Tracker.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_trayIconHandle != null)
        {
            DestroyIcon(_trayIconHandle.Handle);
            _trayIconHandle.Dispose();
            _trayIconHandle = null;
        }

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        Shutdown();
    }

    private static Icon CreateModernTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bgBrush = new LinearGradientBrush(
            new Rectangle(2, 2, 28, 28),
            Color.FromArgb(56, 189, 248),
            Color.FromArgb(99, 102, 241),
            45f);

        g.FillEllipse(bgBrush, 2, 2, 28, 28);

        using var innerBrush = new SolidBrush(Color.FromArgb(20, 22, 27));
        g.FillEllipse(innerBrush, 6, 6, 20, 20);

        using var handPen = new Pen(Color.FromArgb(56, 189, 248), 2.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawLine(handPen, 16, 16, 16, 10);
        g.DrawLine(handPen, 16, 16, 21, 18);

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
