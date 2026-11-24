using System.Runtime.InteropServices;
using System.Threading;

namespace WindowScreenTime.Services;

/// <summary>
/// Çalışan uygulama örneğine başka bir işlemden (ikinci açılış, kurulum sihirbazı, kaldırıcı) haber
/// vermek için Windows'un adlandırılmış olaylarını (named event) kullanır:
/// <list type="bullet">
/// <item><c>ShowWindow</c>: kısayola tekrar tıklanınca çalışan pencereyi öne getirir.</item>
/// <item><c>Exit</c>: kurulum / kaldırma öncesi uygulamanın düzgün kapanmasını (son sürelerin kaydedilmesini) ister.</item>
/// </list>
/// Olaylar yalnızca aynı Windows oturumunda geçerlidir (<c>Local\</c>). Ağ, dosya veya kayıt defteri kullanılmaz.
/// Olay adları kurulum sihirbazında (Installer/SetupWindow.xaml.cs) da kullanılır; değiştirilirse ikisi birlikte değişmelidir.
/// </summary>
public sealed class InstanceSignals : IDisposable
{
    public const string ShowWindowEventName = @"Local\WindowScreenTime_ShowWindow";
    public const string ExitEventName = @"Local\WindowScreenTime_Exit";

    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _exitWait;

    /// <summary>Çalışan (ilk) örnek tarafından oluşturulur; sinyaller arka plan iş parçacığında gelir.</summary>
    public InstanceSignals(Action onShowRequested, Action onExitRequested)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitEvent, (_, _) => onExitRequested(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>
    /// Çalışan örnekten penceresini öne getirmesini ister. Çalışan örnek bu sinyali tanımıyorsa
    /// (eski sürüm) <c>false</c> döner.
    /// </summary>
    public static bool SignalShowWindow()
    {
        // Kullanıcının tıklamasıyla açılan bu işlem öne çıkma hakkına sahiptir; bu hakkı çalışan örneğe
        // devreder. Aksi halde Windows pencereyi öne getirmek yerine görev çubuğunda yanıp söndürür.
        AllowSetForegroundWindow(ASFW_ANY);
        return Signal(ShowWindowEventName);
    }

    /// <summary>Çalışan örnekten düzgün kapanmasını ister.</summary>
    public static bool SignalExit() => Signal(ExitEventName);

    private static bool Signal(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var handle))
                return false;
            using (handle)
            {
                return handle.Set();
            }
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _showWait.Unregister(null);
        _exitWait.Unregister(null);
        _showEvent.Dispose();
        _exitEvent.Dispose();
    }
}
