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
            catch
            {
                _usageByDate = new Dictionary<string, DailyUsage>();
            }
        }
    }

    public DailyUsage GetOrCreateDay(DateTime date)
    {
        string key = date.ToString("yyyy-MM-dd");
        lock (_syncLock)
        {
            if (!_usageByDate.TryGetValue(key, out var usage))
            {
                usage = new DailyUsage
                {
                    DateKey = key,
                    FirstSeen = date,
                    LastSeen = date
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
        lock (_syncLock)
        {
            foreach (var day in importedDays)
            {
                if (!_usageByDate.TryGetValue(day.DateKey, out var existing))
                {
                    _usageByDate[day.DateKey] = day;
                }
                else
                {
                    existing.SleepSeconds = day.SleepSeconds;

                    if (existing.IsImportedFromEventLog || existing.IdleSeconds == 0)
                    {
                        existing.ActiveSeconds = Math.Max(0, day.ActiveSeconds - existing.IdleSeconds);
                        if (day.FirstSeen.HasValue)
                            existing.FirstSeen = day.FirstSeen;
                        if (day.LastSeen.HasValue && (!existing.LastSeen.HasValue || day.LastSeen > existing.LastSeen))
                            existing.LastSeen = day.LastSeen;
                    }
                    else
                    {
                        if (day.FirstSeen.HasValue && (!existing.FirstSeen.HasValue || day.FirstSeen < existing.FirstSeen))
                            existing.FirstSeen = day.FirstSeen;
                    }
                }
            }
            SaveUsage();
        }
    }

    public void SaveUsage()
    {
        lock (_syncLock)
        {
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
                string json = JsonSerializer.Serialize(Settings, JsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Settings save error: {ex.Message}");
            }
        }
    }

    private static string GetStartupLauncherPath()
    {
        string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupDir, "WindowScreenTime.cmd");
    }

    private static bool IsStartupShortcutPresent()
    {
        try
        {
            return File.Exists(GetStartupLauncherPath());
        }
        catch
        {
            return false;
        }
    }

    private static void UpdateStartupLauncher(bool enable)
    {
        try
        {
            string launcherPath = GetStartupLauncherPath();
            if (enable)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    string content = $"@echo off\r\nstart \"\" \"{exePath}\" --minimized\r\n";
                    File.WriteAllText(launcherPath, content);
                }
            }
            else if (File.Exists(launcherPath))
            {
                File.Delete(launcherPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup update error: {ex.Message}");
        }
    }
}
