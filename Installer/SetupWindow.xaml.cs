using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
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
            await Task.Run(() =>
            {
                int myPid = Environment.ProcessId;
                foreach (var proc in Process.GetProcessesByName("WindowScreenTime"))
                {
                    if (proc.Id != myPid)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                        catch
                        {
                        }
                    }
                }
            });

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

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            object? shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut == null) return;

            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { $"{targetPath},0" });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        catch
        {
            // Yoksay
        }
    }
}
