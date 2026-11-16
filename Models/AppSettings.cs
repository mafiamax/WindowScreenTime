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
}
