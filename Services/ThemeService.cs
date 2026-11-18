using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WindowScreenTime.Services;

/// <summary>
/// Koyu / Açık / Sistem teması arasında geçiş yapar. Renkler Themes/Dark.xaml ve Themes/Light.xaml
/// sözlüklerindedir; uygulama kaynaklarındaki ilk birleştirilmiş sözlük çalışma anında değiştirilir.
/// </summary>
public static class ThemeService
{
    public const string ThemeSystem = "System";
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";

    private const string DarkUri = "pack://application:,,,/WindowScreenTime;component/Themes/Dark.xaml";
    private const string LightUri = "pack://application:,,,/WindowScreenTime;component/Themes/Light.xaml";

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static string _setting = ThemeDark;

    /// <summary>Şu anda uygulanan temanın koyu olup olmadığı.</summary>
    public static bool IsDarkApplied { get; private set; } = true;

    /// <summary>Tema değiştiğinde (renkleri koddan hesaplanan alanların yenilenmesi için) tetiklenir.</summary>
    public static event Action? ThemeChanged;

    public static string Normalize(string? setting) => setting switch
    {
        ThemeSystem => ThemeSystem,
        ThemeLight => ThemeLight,
        _ => ThemeDark
    };

    public static void Apply(string? setting)
    {
        _setting = Normalize(setting);
        bool dark = _setting switch
        {
            ThemeLight => false,
            ThemeSystem => !IsWindowsAppLightTheme(),
            _ => true
        };

        var app = System.Windows.Application.Current;
        if (app == null) return;

        var dictionary = new ResourceDictionary { Source = new Uri(dark ? DarkUri : LightUri) };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = dictionary;
        else
            merged.Add(dictionary);

        IsDarkApplied = dark;

        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke();
    }

    /// <summary>"Sistem" seçiliyse Windows teması değiştiğinde yeniden uygular.</summary>
    public static void ReapplyIfFollowingSystem()
    {
        if (_setting == ThemeSystem)
        {
            Apply(_setting);
        }
    }

    /// <summary>Pencerenin Windows başlık çubuğunu temaya uygun (koyu/açık) yapar.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int value = IsDarkApplied ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
            }
        }
        catch
        {
            // Eski Windows sürümlerinde desteklenmeyebilir: yoksay
        }
    }

    /// <summary>Windows "Uygulama modu" ayarını okur (yalnızca okuma).</summary>
    private static bool IsWindowsAppLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }
}
