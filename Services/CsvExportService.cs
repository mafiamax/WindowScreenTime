using System.Globalization;
using System.IO;
using System.Text;
using WindowScreenTime.Models;

namespace WindowScreenTime.Services;

/// <summary>
/// Kullanım geçmişini Excel'de doğrudan açılabilen CSV dosyasına dönüştürür.
/// Ayırıcı, Windows bölge ayarındaki liste ayırıcısıdır (Türkçe Windows'ta ";").
/// Dosya UTF-8 BOM ile yazılır; böylece Excel Türkçe karakterleri doğru gösterir.
/// </summary>
public static class CsvExportService
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    public static string DefaultSeparator
    {
        get
        {
            string sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            return string.IsNullOrWhiteSpace(sep) ? ";" : sep.Trim();
        }
    }

    public static string BuildCsv(IEnumerable<DailyUsage> days, string separator)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(separator, new[]
        {
            "Tarih", "Gün", "İlk Açılış", "Son Aktiflik",
            "Aktif (dk)", "Boşta (dk)", "Uykuda (dk)", "Toplam Açık (dk)",
            "Aktif"
        }));
        sb.Append("\r\n");

        foreach (var d in days.OrderBy(x => x.DateKey, StringComparer.Ordinal))
        {
            var fields = new[]
            {
                d.DateKey,
                d.ParsedDate.ToString("dddd", TrCulture),
                d.FirstSeen?.ToString("HH:mm") ?? "",
                d.LastSeen?.ToString("HH:mm") ?? "",
                ToMinutes(d.ActiveSeconds),
                ToMinutes(d.IdleSeconds),
                ToMinutes(d.SleepSeconds),
                ToMinutes(d.ActiveSeconds + d.IdleSeconds),
                d.ActiveFormatted
            };
            sb.Append(string.Join(separator, fields.Select(f => Escape(f, separator))));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    public static void WriteCsv(string path, IEnumerable<DailyUsage> days)
    {
        File.WriteAllText(path, BuildCsv(days, DefaultSeparator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string ToMinutes(long seconds) =>
        ((long)Math.Round(seconds / 60.0)).ToString(CultureInfo.InvariantCulture);

    private static string Escape(string value, string separator)
    {
        if (value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
