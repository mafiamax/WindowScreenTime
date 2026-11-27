using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WindowScreenTime.Models;

namespace WindowScreenTime.Services;

public class DataStorageService
{
    private readonly string _dataDir;
    private readonly string _usageFilePath;
    private readonly string _settingsFilePath;
    private readonly object _syncLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private Dictionary<string, DailyUsage> _usageByDate = new();
    public AppSettings Settings { get; private set; } = new();

    public string DataDirectory => _dataDir;

    public DataStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dataDir = Path.Combine(localAppData, "WindowScreenTime");
        Directory.CreateDirectory(_dataDir);

        _usageFilePath = Path.Combine(_dataDir, "usage_history.json");
        _settingsFilePath = Path.Combine(_dataDir, "settings.json");

        LoadAll();
    }

    public void LoadAll()
    {
        lock (_syncLock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                Settings = new AppSettings();
            }

            Settings.RunOnStartup = IsStartupShortcutPresent();

            try
            {
                if (File.Exists(_usageFilePath))
                {
                    string json = File.ReadAllText(_usageFilePath);
                    var list = JsonSerializer.Deserialize<List<DailyUsage>>(json) ?? new List<DailyUsage>();
                    _usageByDate = list
                        .GroupBy(u => u.DateKey)
                        .ToDictionary(g => g.Key, g => g.First());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Usage load error: {ex.Message}");
                _usageByDate = new Dictionary<string, DailyUsage>();

                // Okunamayan geçmiş dosyasını, üzerine yazmadan önce yedekle. Yedek alınamazsa
                // (ör. dosya kilitli) mevcut dosyayı ezmemek için kayıt işlemini devre dışı bırak.
                if (!TryBackupUnreadableUsageFile())
                {
                    _usageSaveBlocked = true;
                }
            }
        }
    }

    private bool _usageSaveBlocked;

    private bool TryBackupUnreadableUsageFile()
    {
        try
        {
            string backupPath = Path.Combine(
                _dataDir,
                $"usage_history.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(_usageFilePath, backupPath, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Usage backup error: {ex.Message}");
            return false;
        }
    }

    public DailyUsage GetOrCreateDay(DateTime date)
    {
        string key = date.ToString("yyyy-MM-dd");
        lock (_syncLock)
        {
            if (!_usageByDate.TryGetValue(key, out var usage))
            {
                // FirstSeen / LastSeen burada ATANMAZ: yalnızca gerçek Aktif/Boşta zaman dilimleri
                // (UsageTrackerService.AllocateTimeAcrossMidnight) bu alanları doldurur. Aksi halde gece
                // yarısını uykuda geçen bir günün "İlk Açılış" saati 00:00 görünür.
                usage = new DailyUsage
                {
                    DateKey = key
                };
                _usageByDate[key] = usage;
            }
            return usage;
        }
    }

    public List<DailyUsage> GetRecentDays(int dayCount)
    {
        lock (_syncLock)
        {
            var result = new List<DailyUsage>(dayCount);
            DateTime today = DateTime.Today;

            for (int i = 0; i < dayCount; i++)
            {
                DateTime targetDate = today.AddDays(-i);
                string key = targetDate.ToString("yyyy-MM-dd");
                if (_usageByDate.TryGetValue(key, out var existing))
                {
                    result.Add(existing);
                }
                else
                {
                    result.Add(new DailyUsage
                    {
                        DateKey = key,
                        ActiveSeconds = 0,
                        IdleSeconds = 0
                    });
                }
            }

            return result;
        }
    }

    public List<DailyUsage> GetAllRecordedDays()
    {
        lock (_syncLock)
        {
            return _usageByDate.Values
                .OrderByDescending(u => u.DateKey)
                .ToList();
        }
    }

    public void MergeImportedDays(IEnumerable<DailyUsage> importedDays)
    {
        // Kural: Olay Günlüğü yalnızca "bilgisayar açık / uykuda" bilgisini verir; kilit, ekran kapalı ve
        // boşta ayrımını bilmez. Bu yüzden uygulamanın canlı ölçtüğü sayaçların ÜZERİNE ASLA yazılmaz.
        // İçe aktarılan veri yalnızca (a) hiç kaydı olmayan günleri, (b) daha önce de Olay Günlüğünden
        // gelmiş günleri ve (c) canlı takipte hiç Aktif/Boşta süresi olmayan geçmiş günleri doldurur.
        // Bugünün kaydına hiç dokunulmaz (canlı takip devam ediyor).
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");

        lock (_syncLock)
        {
            foreach (var day in importedDays)
            {
                if (!_usageByDate.TryGetValue(day.DateKey, out var existing))
                {
                    day.IsImportedFromEventLog = true;
                    _usageByDate[day.DateKey] = day;
                    continue;
                }

                if (day.DateKey == todayKey)
                    continue;

                if (existing.IsImportedFromEventLog)
                {
                    // Önceki içe aktarmanın sonucunu yenisiyle değiştir
                    existing.ActiveSeconds = day.ActiveSeconds;
                    existing.IdleSeconds = 0;
                    existing.SleepSeconds = day.SleepSeconds;
                    existing.FirstSeen = day.FirstSeen;
                    existing.LastSeen = day.LastSeen;
                }
                else if (existing.ActiveSeconds == 0 && existing.IdleSeconds == 0)
                {
                    // Canlı kayıtta yalnızca uyku var (ör. gün boyu uykuda kalan bir gün): boşlukları doldur
                    existing.ActiveSeconds = day.ActiveSeconds;
                    existing.SleepSeconds = Math.Max(existing.SleepSeconds, day.SleepSeconds);
                    existing.FirstSeen = day.FirstSeen;
                    existing.LastSeen = day.LastSeen;
                    existing.IsImportedFromEventLog = true;
                }
                // Aksi halde canlı ölçülmüş gün: hiçbir alana dokunma.
            }
            SaveUsage();
        }
    }

    /// <summary>
    /// "Geçmişi Sıfırla": tüm günlük kayıtları (bugün dahil) siler. Silmeden önce bellekteki geçmişin
    /// tamamı <c>usage_history.reset-&lt;tarih&gt;.json</c> olarak veri klasörüne yedeklenir; yedek
    /// yazılamazsa hiçbir şey silinmez. Ayarlar korunur, yalnızca "Olay Günlüğü tarandı" bilgisi sıfırlanır.
    /// </summary>
    /// <returns>Yedek dosyasının tam yolu.</returns>
    /// <exception cref="InvalidOperationException">Geçmiş dosyası okunamadığı için kayıt kapalıysa.</exception>
    public string ResetAllUsage()
    {
        lock (_syncLock)
        {
            if (_usageSaveBlocked)
            {
                throw new InvalidOperationException(
                    "Geçmiş dosyası okunamadığı ve yedeklenemediği için sıfırlama yapılamıyor.");
            }

            var ordered = _usageByDate.Values.OrderByDescending(x => x.DateKey).ToList();
            string json = JsonSerializer.Serialize(ordered, JsonOptions);
            string backupPath = Path.Combine(_dataDir, $"usage_history.reset-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            // FileMode.CreateNew: aynı isimde bir yedek varsa üzerine yazmaz, hata verir (silme iptal olur).
            using (var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
            }

            _usageByDate = new Dictionary<string, DailyUsage>();
            SaveUsage();

            // Yalnızca ayar dosyası yazılır; Başlangıç klasörü kısayoluna dokunulmaz.
            Settings.HasImportedEventLog = false;
            WriteSettingsFile();

            return backupPath;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Verileri Yedekle / Geri Yükle
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Kullanım geçmişinin ve ayarların tamamını tek bir yedek dosyasına yazar (kullanıcının seçtiği
    /// konuma). Önce geçici dosyaya yazılır, sonra yerine taşınır; yarım yedek dosyası kalmaz.
    /// </summary>
    /// <returns>Yedeğe yazılan gün sayısı.</returns>
    public int ExportBackup(string filePath)
    {
        string json;
        int dayCount;
        lock (_syncLock)
        {
            var days = _usageByDate.Values
                .Where(HasAnyTime)
                .OrderByDescending(x => x.DateKey)
                .ToList();
            dayCount = days.Count;

            var backup = new BackupData
            {
                AppVersion = UpdateService.GetCurrentVersionString(),
                CreatedAt = DateTime.Now,
                MachineName = Environment.MachineName,
                Settings = Settings,
                Usage = days
            };
            json = JsonSerializer.Serialize(backup, JsonOptions);
        }

        string tempFile = filePath + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, filePath, overwrite: true);
        return dayCount;
    }

    /// <summary>
    /// Yedek dosyasını okur ve doğrular. Hiçbir veriyi değiştirmez. Geçersiz tarihli veya negatif süreli
    /// kayıtlar ile hiç süre içermeyen günler ayıklanır.
    /// </summary>
    /// <exception cref="InvalidDataException">Dosya geçerli bir WindowScreenTime yedeği değilse
    /// (mesaj kullanıcıya gösterilebilir).</exception>
    public static BackupData ReadBackup(string filePath)
    {
        const string NotABackup = "Seçilen dosya geçerli bir WindowScreenTime yedeği değil.";

        BackupData? backup;
        try
        {
            backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(filePath));
        }
        catch (JsonException)
        {
            throw new InvalidDataException(NotABackup + "\n\nDosya bozuk veya farklı bir dosya olabilir.");
        }

        if (backup == null || backup.Format != BackupData.FormatName || backup.FormatVersion < 1)
            throw new InvalidDataException(NotABackup);

        if (backup.FormatVersion > BackupData.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                "Bu yedek, uygulamanın daha yeni bir sürümüyle oluşturulmuş.\n\n" +
                "Önce uygulamayı güncelleyip (Hakkında → Güncellemeleri Kontrol Et) sonra tekrar deneyin.");
        }

        if (backup.Usage == null)
            throw new InvalidDataException(NotABackup + "\n\nDosyada kullanım geçmişi bulunamadı.");

        backup.Usage = backup.Usage
            .Where(d => d != null
                        && DateTime.TryParseExact(d.DateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out _)
                        && d.ActiveSeconds >= 0 && d.IdleSeconds >= 0 && d.SleepSeconds >= 0
                        && HasAnyTime(d))
            .GroupBy(d => d.DateKey)
            .Select(g => g.First())
            .OrderByDescending(d => d.DateKey)
            .ToList();

        return backup;
    }

    /// <summary>
    /// Geri yüklemede kaç günün ekleneceğini, kaçının atlanacağını hesaplar (hiçbir şeyi değiştirmez).
    /// Atlananlar: zaten kaydı olan günler, bugün ve ileri tarihli günler.
    /// </summary>
    public RestorePreview PreviewRestore(BackupData backup)
    {
        lock (_syncLock)
        {
            int add = 0, skip = 0;
            foreach (var day in backup.Usage ?? new List<DailyUsage>())
            {
                if (CanRestoreDay(day.DateKey)) add++;
                else skip++;
            }
            return new RestorePreview(add, skip);
        }
    }

    private bool CanRestoreDay(string dateKey)
    {
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        // Bugün canlı takip ediliyor; ileri tarihler (saati yanlış bilgisayar) alınmaz.
        if (string.CompareOrdinal(dateKey, todayKey) >= 0)
            return false;
        // Aynı gün iki tarafta da varsa mevcut kayıt kalır: hiçbir ölçüm kaybolmaz.
        return !_usageByDate.TryGetValue(dateKey, out var existing) || !HasAnyTime(existing);
    }

    /// <summary>
    /// Yedeği mevcut verilerle BİRLEŞTİRİR (üzerine yazmaz): yalnızca mevcut geçmişte olmayan geçmiş
    /// günler eklenir. İstenirse kullanıcı ayarları da yedekten alınır. İşlemden önce mevcut geçmiş (ve
    /// ayarlar alınacaksa ayar dosyası) veri klasörüne yedeklenir; bu yedek yazılamazsa hiçbir şey
    /// değiştirilmez ve hata fırlatılır.
    /// </summary>
    public RestoreResult RestoreBackup(BackupData backup, bool includeSettings)
    {
        lock (_syncLock)
        {
            if (_usageSaveBlocked)
            {
                throw new InvalidOperationException(
                    "Geçmiş dosyası okunamadığı ve yedeklenemediği için geri yükleme yapılamıyor.");
            }

            // 1) Güvenlik yedeği (FileMode.CreateNew: aynı adlı dosyanın üzerine yazmaz)
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            // Aynı saniyede ikinci bir geri yükleme olursa dosya adına sıra numarası eklenir
            for (int n = 2; File.Exists(Path.Combine(_dataDir, $"usage_history.before-restore-{stamp}.json")); n++)
                stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-{n}";

            string safetyPath = Path.Combine(_dataDir, $"usage_history.before-restore-{stamp}.json");
            WriteNewFile(safetyPath,
                JsonSerializer.Serialize(_usageByDate.Values.OrderByDescending(x => x.DateKey).ToList(), JsonOptions));
            if (includeSettings && backup.Settings != null)
            {
                WriteNewFile(Path.Combine(_dataDir, $"settings.before-restore-{stamp}.json"),
                    JsonSerializer.Serialize(Settings, JsonOptions));
            }

            // 2) Günleri birleştir
            int added = 0, skipped = 0;
            foreach (var day in backup.Usage ?? new List<DailyUsage>())
            {
                if (!CanRestoreDay(day.DateKey))
                {
                    skipped++;
                    continue;
                }
                _usageByDate[day.DateKey] = day;
                added++;
            }
            if (added > 0)
                SaveUsage();

            // 3) Ayarlar (yalnızca kullanıcı tercihleri; susturma / bildirim günü gibi anlık durumlar alınmaz)
            bool settingsRestored = false;
            if (includeSettings && backup.Settings is { } s)
            {
                bool startupChanged = Settings.RunOnStartup != s.RunOnStartup;
                int goal = Math.Max(0, s.DailyGoalMinutes);

                Settings.IdleTimeoutMinutes = s.IdleTimeoutMinutes > 0 ? s.IdleTimeoutMinutes : Settings.IdleTimeoutMinutes;
                Settings.BreakReminderMinutes = Math.Max(0, s.BreakReminderMinutes);
                Settings.Theme = ThemeService.Normalize(s.Theme);
                if (Settings.DailyGoalMinutes != goal)
                {
                    Settings.DailyGoalMinutes = goal;
                    Settings.DailyGoalNotifiedDate = null;
                }
                Settings.RunOnStartup = s.RunOnStartup;

                // Başlangıç kısayoluna yalnızca "Windows ile başlat" gerçekten değiştiyse dokunulur
                if (startupChanged) SaveSettings();
                else WriteSettingsFile();
                settingsRestored = true;
            }

            return new RestoreResult(added, skipped, settingsRestored, safetyPath);
        }
    }

    private static void WriteNewFile(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static bool HasAnyTime(DailyUsage d) => d.ActiveSeconds > 0 || d.IdleSeconds > 0 || d.SleepSeconds > 0;

    public void SaveUsage()
    {
        lock (_syncLock)
        {
            if (_usageSaveBlocked)
            {
                Debug.WriteLine("Usage save skipped: history file could not be read or backed up.");
                return;
            }

            try
            {
                var ordered = _usageByDate.Values.OrderByDescending(x => x.DateKey).ToList();
                string json = JsonSerializer.Serialize(ordered, JsonOptions);
                string tempFile = _usageFilePath + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _usageFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Usage save error: {ex.Message}");
            }
        }
    }

    public void SaveSettings()
    {
        lock (_syncLock)
        {
            try
            {
                UpdateStartupLauncher(Settings.RunOnStartup);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup shortcut error: {ex.Message}");
            }
            WriteSettingsFile();
        }
    }

    /// <summary>
    /// Yalnızca settings.json'u yazar; Başlangıç klasörü kısayoluna dokunmaz. Kullanıcının "Windows ile
    /// başlat" ayarıyla ilgisi olmayan otomatik kayıtlarda (mola susturma, günlük hedef bildirimi) kullanılır.
    /// </summary>
    public void SaveSettingsFileOnly() => WriteSettingsFile();

    private void WriteSettingsFile()
    {
        lock (_syncLock)
        {
            try
            {
                string json = JsonSerializer.Serialize(Settings, JsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Settings save error: {ex.Message}");
            }
        }
    }

    // Başlangıç klasörü kısayolu StartupShortcutService içinde yönetilir.
    private static bool IsStartupShortcutPresent() => StartupShortcutService.IsEnabled();

    private static void UpdateStartupLauncher(bool enable) => StartupShortcutService.SetEnabled(enable);
}
