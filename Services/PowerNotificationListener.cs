using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowScreenTime.Services;

/// <summary>
/// Windows ekran kapanma / Modern Standby (GUID_CONSOLE_DISPLAY_state) olaylarını yakalayan
/// ve uygulamanın uykuyu engellemediğini Windows Güç Yöneticisine bildiren yardımcı sınıf.
/// </summary>
public sealed class PowerNotificationListener : NativeWindow, IDisposable
{
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    private const uint ES_CONTINUOUS = 0x80000000;

    // GUID_CONSOLE_DISPLAY_state: Ekran açık (1), kapalı/uykuda (0), kısık (2)
    private static Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(
        IntPtr hRecipient,
        ref Guid PowerSettingGuid,
        int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private IntPtr _registrationHandle = IntPtr.Zero;

    public event Action<bool>? DisplayPowerStateChanged;

    public PowerNotificationListener()
    {
        // Uygulamanın uyku veya ekran kapanmasını engellemediğini açıkça bildir
        AllowSystemSleep();

        CreateHandle(new CreateParams
        {
            Caption = "WindowScreenTime_PowerListener"
        });

        if (Handle != IntPtr.Zero)
        {
            _registrationHandle = RegisterPowerSettingNotification(
                Handle,
                ref GuidConsoleDisplayState,
                DEVICE_NOTIFY_WINDOW_HANDLE);
        }
    }

    public static void AllowSystemSleep()
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch
        {
            // Yoksay
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
            if (setting.PowerSetting == GuidConsoleDisplayState)
            {
                // 0 = Ekran Kapandı (Uyku/Standby başlangıcı), 1 = Ekran Açıldı, 2 = Kısıldı
                bool isDisplayOn = setting.Data != 0;
                DisplayPowerStateChanged?.Invoke(isDisplayOn);
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registrationHandle != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_registrationHandle);
            _registrationHandle = IntPtr.Zero;
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
