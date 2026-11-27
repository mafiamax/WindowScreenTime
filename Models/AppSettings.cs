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
    /// Kesintisiz bu kadar dakika Aktif kullanımdan sonra mola hatırlatması gösterilir.
    /// 0 = kapalı. Varsayılan: 50 dakika.
    /// </summary>
    public int BreakReminderMinutes { get; set; } = 50;

    /// <summary>
    /// Arayüz teması: "Dark", "Light" veya "System" (Windows ayarını izler). Varsayılan: "Dark".
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Mola hatırlatıcısı bu ana kadar susturuldu (tepsi menüsü: "1 saat" / "bugünlük").
    /// Boş veya geçmiş bir zaman = susturma yok. Uygulama yeniden başlasa da geçerlidir.
    /// </summary>
    public DateTime? BreakReminderMutedUntil { get; set; }

    /// <summary>
    /// Günlük Aktif kullanım hedefi (dakika). Bugünkü Aktif süre bu değere ulaşınca bir kez tepsi
    /// bildirimi gösterilir ve grafikte hedef çizgisi çizilir. 0 = kapalı (varsayılan).
    /// </summary>
    public int DailyGoalMinutes { get; set; } = 0;

    /// <summary>
    /// Günlük hedef bildiriminin en son gösterildiği gün (yyyy-MM-dd). Aynı gün ikinci kez
    /// bildirim gösterilmez; hedef değiştirilince temizlenir.
    /// </summary>
    public string? DailyGoalNotifiedDate { get; set; }
}
