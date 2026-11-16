using Microsoft.Win32;
using System.Windows.Threading;
using WindowScreenTime.Models;

namespace WindowScreenTime.Services;

public class UsageTrackerService : IDisposable
{
    private enum TimeBucket
    {
        Active,
        Idle,
        Sleep
    }

    private readonly DataStorageService _storage;
    private readonly DispatcherTimer _timer;
    private readonly PowerNotificationListener _powerListener;
    private DateTime _lastTickTime;
    private DateTime? _sleepStartTime;
    private bool _isLockedOrSleeping;
    private bool _isCurrentlyIdle;
    private int _activeTicksSinceLastSave;

    public event Action? StatsUpdated;

    public bool IsCurrentlyIdle => _isCurrentlyIdle;
    public bool IsLockedOrSleeping => _isLockedOrSleeping;

    public UsageTrackerService(DataStorageService storage)
    {
        _storage = storage;
        _lastTickTime = DateTime.Now;

        var today = _storage.GetOrCreateDay(_lastTickTime);
        if (!today.FirstSeen.HasValue)
            today.FirstSeen = _lastTickTime;
        today.LastSeen = _lastTickTime;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _timer.Tick += OnTimerTick;

        _powerListener = new PowerNotificationListener();
        _powerListener.DisplayPowerStateChanged += OnDisplayPowerStateChanged;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    public void Start()
    {
        PowerNotificationListener.AllowSystemSleep();
        _lastTickTime = DateTime.Now;
        _timer.Start();
    }

    public async Task<int> ImportEventLogHistoryAsync()
    {
        var imported = await Task.Run(() => EventLogImporter.ImportRecentDaysFromSystemLog(30));
        if (imported.Count > 0)
        {
            _storage.MergeImportedDays(imported);
        }
        _storage.Settings.HasImportedEventLog = true;
        _storage.SaveSettings();
        StatsUpdated?.Invoke();
        return imported.Count;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        ProcessElapsedUntilNow();
    }

    public void ProcessElapsedUntilNow()
    {
        DateTime now = DateTime.Now;
        TimeSpan elapsed = now - _lastTickTime;

        if (elapsed.TotalSeconds <= 0)
        {
            _lastTickTime = now;
            return;
        }

        if (_isLockedOrSleeping)
        {
            _lastTickTime = now;
            return;
        }

        // Eğer zamanlayıcı > 2 dakika atladıysa bilgisayar Modern Standby / uykudan yeni uyanmıştır
        if (elapsed.TotalMinutes > 2)
        {
            AllocateTimeAcrossMidnight(_lastTickTime, now, TimeBucket.Sleep);
            _lastTickTime = now;
            _storage.SaveUsage();
            StatsUpdated?.Invoke();
            return;
        }

        TimeSpan idleTime = IdleDetector.GetIdleTime();
        TimeSpan idleThreshold = TimeSpan.FromMinutes(Math.Max(1, _storage.Settings.IdleTimeoutMinutes));
        bool wasIdle = _isCurrentlyIdle;
        _isCurrentlyIdle = idleTime >= idleThreshold;

        AllocateTimeAcrossMidnight(
            _lastTickTime,
            now,
            _isCurrentlyIdle ? TimeBucket.Idle : TimeBucket.Active);

        _lastTickTime = now;

        if (_isCurrentlyIdle)
        {
            // Kullanıcı yeni Boşta (Idle) moduna geçtiyse yalnızca bir kez diske kaydet,
            // boşta kaldığı sürece diske yazma yapma (Windows'un uykuya geçişini ve disk/antivirüs aktivitesini tetiklememek için).
            if (!wasIdle)
            {
                _storage.SaveUsage();
                _activeTicksSinceLastSave = 0;
                PowerNotificationListener.AllowSystemSleep();
                StatsUpdated?.Invoke();
            }
        }
        else
        {
            _activeTicksSinceLastSave++;

            // Aktif kullanımda her 12 adımda bir (2 dakikada bir) veya Idle'dan çıkınca kaydet
            if (wasIdle || _activeTicksSinceLastSave >= 12)
            {
                _storage.SaveUsage();
                _activeTicksSinceLastSave = 0;
            }

            StatsUpdated?.Invoke();
        }
    }

    private void EnterSleepOrLockState()
    {
        if (!_isLockedOrSleeping)
        {
            ProcessElapsedUntilNow();
            _isLockedOrSleeping = true;
            _sleepStartTime = DateTime.Now;
            _timer.Stop(); // Uyku / ekran kapalıyken zamanlayıcıyı tamamen durdur (0 arka plan aktivitesi)
            _storage.SaveUsage();
            PowerNotificationListener.AllowSystemSleep();
            StatsUpdated?.Invoke();
        }
    }

    private void ResumeFromSleepOrLockState()
    {
        if (_isLockedOrSleeping)
        {
            DateTime now = DateTime.Now;
            if (_sleepStartTime.HasValue && now > _sleepStartTime.Value)
            {
                AllocateTimeAcrossMidnight(_sleepStartTime.Value, now, TimeBucket.Sleep);
                _sleepStartTime = null;
            }

            _isLockedOrSleeping = false;
            _isCurrentlyIdle = false;
            _lastTickTime = now;

            var today = _storage.GetOrCreateDay(now);
            today.LastSeen = now;

            _storage.SaveUsage();
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
            StatsUpdated?.Invoke();
        }
    }

    private void OnDisplayPowerStateChanged(bool isDisplayOn)
    {
        if (!isDisplayOn)
        {
            EnterSleepOrLockState();
        }
        else
        {
            ResumeFromSleepOrLockState();
        }
    }

    private void AllocateTimeAcrossMidnight(DateTime start, DateTime end, TimeBucket bucket)
    {
        DateTime cursor = start;
        while (cursor < end)
        {
            DateTime nextMidnight = cursor.Date.AddDays(1);
            DateTime segmentEnd = end < nextMidnight ? end : nextMidnight;
            long seconds = (long)Math.Round((segmentEnd - cursor).TotalSeconds);

            if (seconds > 0)
            {
                DailyUsage dayUsage = _storage.GetOrCreateDay(cursor);

                if (bucket == TimeBucket.Sleep)
                {
                    dayUsage.SleepSeconds += seconds;
                }
                else if (bucket == TimeBucket.Idle)
                {
                    if (!dayUsage.FirstSeen.HasValue || cursor < dayUsage.FirstSeen.Value)
                        dayUsage.FirstSeen = cursor;
                    dayUsage.IdleSeconds += seconds;
                }
                else
                {
                    if (!dayUsage.FirstSeen.HasValue || cursor < dayUsage.FirstSeen.Value)
                        dayUsage.FirstSeen = cursor;
                    dayUsage.ActiveSeconds += seconds;
                    dayUsage.LastSeen = segmentEnd;
                }
            }

            cursor = segmentEnd;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect)
        {
            EnterSleepOrLockState();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            ResumeFromSleepOrLockState();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            EnterSleepOrLockState();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            ResumeFromSleepOrLockState();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        if (!_isLockedOrSleeping)
        {
            ProcessElapsedUntilNow();
        }
        else if (_sleepStartTime.HasValue)
        {
            AllocateTimeAcrossMidnight(_sleepStartTime.Value, DateTime.Now, TimeBucket.Sleep);
            _sleepStartTime = null;
        }
        _storage.SaveUsage();
    }

    public DailyUsage GetTodayUsage()
    {
        return _storage.GetOrCreateDay(DateTime.Today);
    }

    public string GetAverageActiveFormatted(int days)
    {
        var recent = _storage.GetRecentDays(days)
            .Where(d => d.ActiveSeconds > 0)
            .ToList();

        if (recent.Count == 0)
            return "0 dk";

        long avgSeconds = (long)recent.Average(d => d.ActiveSeconds);
        return DailyUsage.FormatDuration(avgSeconds);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (!_isLockedOrSleeping)
        {
            ProcessElapsedUntilNow();
        }
        _storage.SaveUsage();

        _powerListener.DisplayPowerStateChanged -= OnDisplayPowerStateChanged;
        _powerListener.Dispose();

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }
}
