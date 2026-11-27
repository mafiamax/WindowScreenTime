using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace WindowScreenTime.Models;

public class DailyUsage : INotifyPropertyChanged
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Tablodaki satırın tüm hücrelerini yeniler. Tablo kaynağını baştan yüklemek yerine kullanılır;
    /// böylece kaydırma konumu ve seçili satır korunur.
    /// </summary>
    public void NotifyAllChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    /// <summary>
    /// yyyy-MM-dd formatında tarih anahtarı.
    /// </summary>
    public string DateKey { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

    public long ActiveSeconds { get; set; }
    public long IdleSeconds { get; set; }
    public long SleepSeconds { get; set; }
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }

    [JsonIgnore]
    public DateTime ParsedDate =>
        DateTime.TryParseExact(DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.Today;

    [JsonIgnore]
    public string DisplayDate => ParsedDate.ToString("dd MMM yyyy, ddd", TrCulture);

    [JsonIgnore]
    public string ShortDayLabel => ParsedDate.ToString("dd MMM", TrCulture);

    [JsonIgnore]
    public string FirstSeenFormatted => FirstSeen?.ToString("HH:mm") ?? "--:--";

    [JsonIgnore]
    public string LastSeenFormatted => LastSeen?.ToString("HH:mm") ?? "--:--";

    [JsonIgnore]
    public string ActiveFormatted => FormatDuration(ActiveSeconds);

    [JsonIgnore]
    public string IdleFormatted => FormatDuration(IdleSeconds);

    [JsonIgnore]
    public string SleepFormatted => FormatDuration(SleepSeconds);

    [JsonIgnore]
    public string TotalFormatted => FormatDuration(ActiveSeconds + IdleSeconds);

    [JsonIgnore]
    public double ActiveHours => Math.Round(ActiveSeconds / 3600.0, 2);

    public static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds <= 0)
            return "0 dk";

        var ts = TimeSpan.FromSeconds(totalSeconds);
        int totalHours = (int)ts.TotalHours;

        if (totalHours > 0)
            return $"{totalHours} sa {ts.Minutes} dk";

        if (ts.Minutes > 0)
            return $"{ts.Minutes} dk";

        return $"{ts.Seconds} sn";
    }
}
