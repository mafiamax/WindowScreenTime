using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace WindowScreenTime.Setup;

public partial class SetupWindow : Window
{
    private const string UninstallRegPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowScreenTime";
    private readonly string _installDir;

    public SetupWindow()
    {
        InitializeComponent();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _installDir = Path.Combine(localAppData, "Programs", "WindowScreenTime");
        InstallPathText.Text = _installDir;

        // Sürüm, kurulum programının kendi sürümünden okunur (iki .csproj'daki <Version> her zaman aynıdır;
        // yayın betiği bunu denetler). Böylece başlıkta eski bir sürüm numarası kalmaz.
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        string version = v == null ? "" : $"v{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        if (version.Length > 0)
        {
            Title = $"WindowScreenTime {version} Kurulum Sihirbazı";
            SubtitleText.Text = $"Windows 11 Ekran ve Kullanım Süresi Takipçisi ({version})";
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;

        bool createDesktop = DesktopShortcutCheck.IsChecked == true;
        bool createStartMenu = StartMenuShortcutCheck.IsChecked == true;
        bool launchAfter = LaunchAfterInstallCheck.IsChecked == true;

        try
        {
            StatusText.Text = "Açık olan eski sürüm kapatılıyor...";
            await Task.Run(CloseRunningApp);

            StatusText.Text = "Dosyalar kurulum dizinine kopyalanıyor...";
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_installDir);

                using Stream? payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
                if (payloadStream == null)
                    throw new InvalidOperationException("Kurulum paketi verisi (payload.zip) bulunamadı.");

                using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(_installDir, overwriteFiles: true);
            });

            string installedExe = Path.Combine(_installDir, "WindowScreenTime.exe");

            StatusText.Text = "Kısayollar ve sistem kayıtları oluşturuluyor...";
            if (createDesktop)
            {
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "WindowScreenTime.lnk");
                CreateShortcut(desktopLnk, installedExe, _installDir, "Windows 11 Ekran ve Kullanım Süresi Takipçisi");
            }

            if (createStartMenu)
            {
                string startMenuLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "WindowScreenTime.lnk");
                CreateShortcut(startMenuLnk, installedExe, _installDir, "Windows 11 Ekran ve Kullanım Süresi Takipçisi");
            }

            RegisterUninstaller(installedExe);

            StatusText.Text = "Kurulum başarıyla tamamlandı!";

            if (launchAfter && File.Exists(installedExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installedExe,
                    WorkingDirectory = _installDir,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(
                    "WindowScreenTime bilgisayarınıza başarıyla kuruldu!",
                    "WindowScreenTime Kurulumu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kurulum sırasında hata oluştu.";
            MessageBox.Show(
                $"Kurulum tamamlanamadı:\n{ex.Message}",
                "WindowScreenTime Kurulum Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            InstallBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
        }
    }

    // Ana uygulamadaki Services/InstanceSignals.cs ile aynı ad olmalıdır.
    private const string ExitEventName = @"Local\WindowScreenTime_Exit";

    /// <summary>
    /// Çalışan WindowScreenTime'dan önce düzgün kapanmasını ister (son kullanım süresi kaydedilir ve
    /// tepsi simgesi kaldırılır). 5 saniyede kapanmazsa veya sinyali tanımayan eski bir sürümse zorla kapatır.
    /// </summary>
    private static void CloseRunningApp()
    {
        bool signaled = false;
        try
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEvent))
            {
                using (exitEvent)
                {
                    signaled = exitEvent.Set();
                }
            }
        }
        catch
        {
        }

        int myPid = Environment.ProcessId;
        foreach (var proc in Process.GetProcessesByName("WindowScreenTime"))
        {
            if (proc.Id == myPid)
                continue;
            try
            {
                if (!signaled || !proc.WaitForExit(5000))
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
            }
        }
    }

    private void RegisterUninstaller(string installedExe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallRegPath);
            if (key == null) return;

            key.SetValue("DisplayName", "WindowScreenTime");
            key.SetValue("DisplayVersion", GetInstalledVersion(installedExe));
            key.SetValue("Publisher", "Erhan.M");
            key.SetValue("DisplayIcon", installedExe);
            key.SetValue("InstallLocation", _installDir);
            key.SetValue("UninstallString", $"\"{installedExe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 512, RegistryValueKind.DWord);
        }
        catch
        {
            // Yoksay
        }
    }

    /// <summary>
    /// Program Ekle/Kaldır'da gösterilecek sürümü, kurulan WindowScreenTime.exe dosyasının kendi
    /// sürüm bilgisinden okur (elle yazılmış sabit değer zamanla eskir).
    /// </summary>
    private static string GetInstalledVersion(string installedExe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(installedExe);
            if (info.FileMajorPart > 0 || info.FileMinorPart > 0 || info.FileBuildPart > 0)
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        }
        catch
        {
            // Yoksay
        }

        var own = Assembly.GetExecutingAssembly().GetName().Version;
        return own == null ? "1.0.0" : $"{own.Major}.{own.Minor}.{Math.Max(0, own.Build)}";
    }

    /// <summary>
    /// Masaüstü / Başlat menüsü kısayolunu Windows'un kendi IShellLink COM arayüzüyle oluşturur
    /// (ana uygulamadaki StartupShortcutService ile aynı yöntem; WScript.Shell kullanılmaz — KURAL 2).
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(targetPath, 0);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        catch
        {
            // Yoksay
        }
        finally
        {
            if (link != null)
                Marshal.FinalReleaseComObject(link);
        }
    }

    // --- Windows kabuk kısayolu (IShellLink) COM tanımları ---

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
