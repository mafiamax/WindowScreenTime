using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowScreenTime.Services;

/// <summary>
/// "Windows ile başlat" ayarını, kullanıcının Başlangıç klasöründeki bir kısayolla (.lnk) yönetir.
///
/// Kurallar (PROJECT_HANDOFF.md, KURAL 2):
/// - Registry Run anahtarı KULLANILMAZ (Kaspersky sezgisel yanlış pozitifi).
/// - Eski sürümler bir .cmd komut dosyası kullanıyordu; bu oturum açılışında kısa bir komut penceresi
///   gösteriyordu. Kısayol uygulamayı doğrudan açtığı için hiçbir pencere görünmez.
/// - Kısayol, Windows'un kendi IShellLink arayüzüyle oluşturulur (WScript.Shell / betik kullanılmaz).
/// </summary>
public static class StartupShortcutService
{
    private const string FileBaseName = "WindowScreenTime";
    private const string StartupArguments = "--minimized";

    private static string StartupDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string ShortcutPath => Path.Combine(StartupDirectory, FileBaseName + ".lnk");

    /// <summary>Eski sürümlerin kullandığı komut dosyası.</summary>
    public static string LegacyCmdPath => Path.Combine(StartupDirectory, FileBaseName + ".cmd");

    public static string? CurrentExePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            return File.Exists(ShortcutPath) || File.Exists(LegacyCmdPath);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            if (enable)
            {
                string? exe = CurrentExePath;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    CreateShortcut(ShortcutPath, exe);
                    DeleteIfExists(LegacyCmdPath);
                }
            }
            else
            {
                DeleteIfExists(ShortcutPath);
                DeleteIfExists(LegacyCmdPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup shortcut error: {ex.Message}");
        }
    }

    /// <summary>
    /// Uygulama açılışında çağrılır:
    /// - Eski .cmd dosyası varsa, aynı exe'yi gösteren bir kısayolla değiştirilir ve .cmd silinir.
    /// - Kısayol var ama gösterdiği exe artık yoksa (ör. kurulum yeri değiştiyse) çalışan exe'ye yönlendirilir.
    /// Kısayol, geçerli bir hedefi varken başka bir exe'ye (ör. geliştirme klasöründeki derlemeye) çevrilmez.
    /// </summary>
    public static void MigrateAndRepair() => MigrateAndRepair(StartupDirectory, CurrentExePath);

    /// <summary>Test edilebilirlik için klasör ve çalışan exe parametre olarak verilebilir.</summary>
    public static void MigrateAndRepair(string startupDirectory, string? currentExe)
    {
        string shortcutPath = Path.Combine(startupDirectory, FileBaseName + ".lnk");
        string legacyCmdPath = Path.Combine(startupDirectory, FileBaseName + ".cmd");

        try
        {
            if (File.Exists(legacyCmdPath))
            {
                string? target = ReadExeFromLegacyCmd(legacyCmdPath);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                    target = currentExe;

                if (!string.IsNullOrWhiteSpace(target))
                {
                    CreateShortcut(shortcutPath, target);
                    if (File.Exists(shortcutPath))
                        File.Delete(legacyCmdPath);
                }
                return;
            }

            if (File.Exists(shortcutPath))
            {
                string? target = ReadShortcutTarget(shortcutPath);
                if ((string.IsNullOrWhiteSpace(target) || !File.Exists(target)) && !string.IsNullOrWhiteSpace(currentExe))
                {
                    CreateShortcut(shortcutPath, currentExe);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup shortcut migration error: {ex.Message}");
        }
    }

    /// <summary>Eski .cmd içeriğinden (start "" "C:\...\WindowScreenTime.exe" --minimized) exe yolunu okur.</summary>
    public static string? ReadExeFromLegacyCmd(string cmdPath)
    {
        try
        {
            string content = File.ReadAllText(cmdPath);
            var match = Regex.Match(content, "\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    public static void CreateShortcut(string shortcutPath, string exePath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exePath);
            link.SetArguments(StartupArguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");
            link.SetDescription("Windows Screen Time - sistem tepsisinde başlat");
            link.SetIconLocation(exePath, 0);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static string? ReadShortcutTarget(string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.Length == 0 ? null : sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static string? ReadShortcutArguments(string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);
            var sb = new StringBuilder(1024);
            link.GetArguments(sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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
