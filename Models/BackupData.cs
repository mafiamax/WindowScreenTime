namespace WindowScreenTime.Models;

/// <summary>
/// "Verileri Yedekle" ile kullanıcının seçtiği konuma yazılan tek dosyalık yedek: kullanım geçmişi +
/// ayarlar. Başka bilgisayara taşınabilir. <see cref="FormatVersion"/> ileride biçim değişirse eski
/// yedeklerin tanınıp okunabilmesi için tutulur.
/// </summary>
public class BackupData
{
    public const string FormatName = "WindowScreenTime-Backup";
    public const int CurrentFormatVersion = 1;

    /// <summary>Dosyanın bir WindowScreenTime yedeği olduğunu gösteren sabit ad.</summary>
    public string Format { get; set; } = FormatName;

    /// <summary>Yedek biçiminin sürümü (uygulama sürümünden bağımsız).</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Yedeği oluşturan uygulama sürümü (bilgi amaçlı).</summary>
    public string AppVersion { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>Yedeğin alındığı bilgisayarın adı (bilgi amaçlı).</summary>
    public string MachineName { get; set; } = "";

    public AppSettings? Settings { get; set; }

    public List<DailyUsage>? Usage { get; set; }
}

/// <summary>Geri yükleme öncesi özet: kaç gün eklenecek, kaçı zaten mevcut olduğu için atlanacak.</summary>
public record RestorePreview(int DaysToAdd, int DaysSkipped);

/// <summary>Geri yükleme sonucu.</summary>
public record RestoreResult(int DaysAdded, int DaysSkipped, bool SettingsRestored, string SafetyBackupPath);
