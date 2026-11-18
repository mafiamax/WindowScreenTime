namespace WindowScreenTime.Models;

public class AppSettings
{
    /// <summary>
    /// Kullanıcı bu süre (dakika) boyunca klavye/fareye dokunmazsa "Boşta (Idle)" sayılır.
    /// Varsayılan: 5 dakika.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Windows açılışında otomatik başlatılsın mı?
    /// </summary>
    public bool RunOnStartup { get; set; } = false;

    /// <summary>
    /// İlk kurulumda Windows Olay Günlüğü geçmişi içeri aktarıldı mı?
    /// </summary>
    public bool HasImportedEventLog { get; set; } = false;

    /// <summary>
    /// Kesintisiz bu kadar dakika Aktif kullanımdan sonra mola hatırlatması gösterilir.
    /// 0 = kapalı. Varsayılan: 50 dakika.
    /// </summary>
    public int BreakReminderMinutes { get; set; } = 50;

    /// <summary>
    /// Arayüz teması: "Dark", "Light" veya "System" (Windows ayarını izler). Varsayılan: "Dark".
    /// </summary>
    public string Theme { get; set; } = "Dark";
}
