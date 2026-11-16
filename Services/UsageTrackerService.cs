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

    // Zamanlayıcı bu süreden uzun atladıysa bilgisayarın uykudan / Modern Standby'dan döndüğü kabul edilir.
    private static readonly TimeSpan SleepGapThreshold = TimeSpan.FromMinutes(2);

    private readonly DataStorageService _storage;
    private readonly DispatcherTimer _timer;
    private readonly PowerNotificationListener _powerListener;

    // Geçen süre, sistem saatinden (DateTime.Now) değil monoton sayaçtan (Environment.TickCount64)
    // ölçülür. Böylece yaz saati geçişi veya saatin elle/NTP ile düzeltilmesi "uyku" olarak sayılmaz.
    // TickCount64 (GetTickCount64) uyku/hazırda bekleme süresini de sayar.
    private DateTime _lastTickTime;
    private long _lastTickMono;

    // Duraklatma (uyku sayacı) durumu: kilit, ekran kapalı ve sistem uykusu AYRI ayrı izlenir.
    // Takip ancak üçü de normale döndüğünde devam eder (ör. kilit ekranında ekran yeniden açılınca
    // takip başlamaz).
    private bool _isLocked;
    private bool _isDisplayOff;
    private bool _isSuspended;
    private bool _isPaused;
    private long _pauseStartMono;

    private bool _isCurrentlyIdle;
    // Kesintisiz Aktif zaman diliminin başladığı an. Boşta moduna geçişte geriye dönük düzeltme
    // bu andan daha geriye gitmez (uyku veya önceki boşta süresine taşmamak için).
    private DateTime? _activeRunStart;
    private int _activeTicksSinceLastSave;

    public event Action? StatsUpdated;

    public bool IsCurrentlyIdle => _isCurrentlyIdle;
    public bool IsLockedOrSleeping => _isPaused;

    public UsageTrackerService(DataStorageService storage)
    {
        _storage = storage;
        _lastTickTime = DateTime.Now;
        _lastTickMono = Environment.TickCount64;

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
        _lastTickMono = Environment.TickCount64;
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

    /// <summary>
    /// Kullanıcının uygulamayla doğrudan etkileşime girdiği (ör. pencereyi etkinleştirdiği) anlarda çağrılır.
    /// Bu anda oturum kilitli, ekran kapalı veya sistem uykuda olamaz; kaçırılmış bir kilit açma /
    /// ekran açılma olayı yüzünden takibin duraklatılmış halde takılı kalmasını önler.
    /// </summary>
    public void NotifyUserPresent()
    {
        _isLocked = false;
        _isDisplayOff = false;
        _isSuspended = false;
        UpdatePauseState();
        ProcessElapsedUntilNow();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        ProcessElapsedUntilNow();
    }

    public void ProcessElapsedUntilNow()
    {
        DateTime now = DateTime.Now;
        long nowMono = Environment.TickCount64;
        long elapsedMs = nowMono - _lastTickMono;

        _lastTickTime = now;
        _lastTickMono = nowMono;

        if (_isPaused || elapsedMs <= 0)
            return;

        TimeSpan elapsed = TimeSpan.FromMilliseconds(elapsedMs);
        DateTime start = now - elapsed;

        // Zamanlayıcı uzun süre atladıysa bilgisayar Modern Standby / uykudan yeni uyanmıştır
        if (elapsed > SleepGapThreshold)
        {
            AllocateTimeAcrossMidnight(start, now, TimeBucket.Sleep);
            _isCurrentlyIdle = false;
            _activeRunStart = null;
            _storage.SaveUsage();
            StatsUpdated?.Invoke();
            return;
        }

        TimeSpan idleTime = IdleDetector.GetIdleTime();
        TimeSpan idleThreshold = TimeSpan.FromMinutes(Math.Max(1, _storage.Settings.IdleTimeoutMinutes));
        bool wasIdle = _isCurrentlyIdle;
        _isCurrentlyIdle = idleTime >= idleThreshold;

        AllocateTimeAcrossMidnight(start, now, _isCurrentlyIdle ? TimeBucket.Idle : TimeBucket.Active);

        if (_isCurrentlyIdle)
        {
            if (!wasIdle)
            {
                // Boşta eşiği dolana kadar geçen süre (son girdiden bu ana kadar) Aktif olarak yazılmıştı.
                // Bu süreyi geriye dönük olarak Boşta'ya taşı. Kesintisiz Aktif dilimin başından öteye gitme.
                DateTime lastInput = now - idleTime;
                DateTime reclassifyFrom = _activeRunStart.HasValue && _activeRunStart.Value > lastInput
                    ? _activeRunStart.Value
                    : lastInput;
                if (reclassifyFrom < start)
                {
                    ReclassifyActiveToIdle(reclassifyFrom, start);
                }
                _activeRunStart = null;

                // Kullanıcı yeni Boşta (Idle) moduna geçtiyse yalnızca bir kez diske kaydet,
                // boşta kaldığı sürece diske yazma yapma (Windows'un uykuya geçişini ve disk/antivirüs aktivitesini tetiklememek için).
                _storage.SaveUsage();
                _activeTicksSinceLastSave = 0;
                PowerNotificationListener.AllowSystemSleep();
                StatsUpdated?.Invoke();
            }
        }
        else
        {
            _activeRunStart ??= start;
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

    private void UpdatePauseState()
    {
        bool shouldPause = _isLocked || _isDisplayOff || _isSuspended;
        if (shouldPause && !_isPaused)
        {
            EnterPause();
        }
        else if (!shouldPause && _isPaused)
        {
            ExitPause();
        }
    }

    private void EnterPause()
    {
        ProcessElapsedUntilNow();
        _isPaused = true;
        _pauseStartMono = _lastTickMono;
        _isCurrentlyIdle = false;
        _activeRunStart = null;
        _timer.Stop(); // Uyku / kilit / ekran kapalıyken zamanlayıcıyı tamamen durdur (0 arka plan aktivitesi)
        _storage.SaveUsage();
        PowerNotificationListener.AllowSystemSleep();
        StatsUpdated?.Invoke();
    }

    private void ExitPause()
    {
        DateTime now = DateTime.Now;
        long nowMono = Environment.TickCount64;

        AllocatePausedTimeUntil(now, nowMono);

        _isPaused = false;
        _isCurrentlyIdle = false;
        _activeRunStart = null;
        _lastTickTime = now;
        _lastTickMono = nowMono;

        var today = _storage.GetOrCreateDay(now);
        if (!today.FirstSeen.HasValue)
            today.FirstSeen = now;
        today.LastSeen = now;

        _storage.SaveUsage();
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
        StatsUpdated?.Invoke();
    }

    /// <summary>
    /// Duraklatma başlangıcından verilen ana kadar geçen süreyi Uyku olarak yazar ve
    /// duraklatma başlangıcını bu ana taşır (aynı süre iki kez sayılmaz).
    /// </summary>
    private void AllocatePausedTimeUntil(DateTime now, long nowMono)
    {
        long pausedMs = nowMono - _pauseStartMono;
        if (pausedMs > 0)
        {
            AllocateTimeAcrossMidnight(now - TimeSpan.FromMilliseconds(pausedMs), now, TimeBucket.Sleep);
        }
        _pauseStartMono = nowMono;
    }

    private void OnDisplayPowerStateChanged(bool isDisplayOn)
    {
        _isDisplayOff = !isDisplayOn;
        UpdatePauseState();
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
                    // Uyku süresi FirstSeen / LastSeen alanlarına dokunmaz
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

    private void ReclassifyActiveToIdle(DateTime start, DateTime end)
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
                long moved = Math.Min(seconds, dayUsage.ActiveSeconds);
                dayUsage.ActiveSeconds -= moved;
                dayUsage.IdleSeconds += moved;

                // Son aktif an, son klavye/fare girdisinden sonrası olamaz
                if (dayUsage.LastSeen.HasValue && dayUsage.LastSeen.Value > cursor)
                    dayUsage.LastSeen = cursor;
            }

            cursor = segmentEnd;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect)
        {
            _isLocked = true;
            UpdatePauseState();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            _isLocked = false;
            UpdatePauseState();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _isSuspended = true;
            UpdatePauseState();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _isSuspended = false;
            UpdatePauseState();
        }
    }

    private void FlushCurrentState()
    {
        if (_isPaused)
        {
            AllocatePausedTimeUntil(DateTime.Now, Environment.TickCount64);
        }
        else
        {
            ProcessElapsedUntilNow();
        }
        _storage.SaveUsage();
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        FlushCurrentState();
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
        FlushCurrentState();

        _powerListener.DisplayPowerStateChanged -= OnDisplayPowerStateChanged;
        _powerListener.Dispose();

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }
}
