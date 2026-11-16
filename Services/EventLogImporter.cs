using System.Diagnostics;
using WindowScreenTime.Models;

namespace WindowScreenTime.Services;

public static class EventLogImporter
{
    public enum PowerEventKind
    {
        /// <summary>İşletim sistemi açılışı (önceki oturumun nasıl bittiğinden bağımsız).</summary>
        Boot,
        /// <summary>Uyku / Modern Standby / hazırda beklemeden uyanış.</summary>
        Wake,
        /// <summary>Uyku / Modern Standby girişi.</summary>
        Sleep,
        /// <summary>Düzgün (temiz) kapanış.</summary>
        Shutdown,
        /// <summary>Güç durumu değiştirmeyen herhangi bir sistem kaydı (çökme anını tahmin etmek için kullanılır).</summary>
        Other
    }

    private enum PowerState
    {
        Off,
        Active,
        Sleep
    }

    private sealed class StateSegment
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public PowerState State { get; set; }
        public double DurationSeconds => (End - Start).TotalSeconds;
    }

    // Aynı açılışa ait art arda gelen açılış kayıtlarını (Kernel-General 12 ve EventLog 6005) tek açılış sayma penceresi
    private static readonly TimeSpan SameBootWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Son N gün için Windows System Event Log kayıtlarını (Modern Standby 506/507 dahil) tarayarak
    /// Aktif ve Uykuda geçen süreleri hesaplar.
    /// </summary>
    public static List<DailyUsage> ImportRecentDaysFromSystemLog(int maxDaysBack = 30)
    {
        DateTime cutoffDate = DateTime.Today.AddDays(-maxDaysBack);
        var events = new List<(DateTime Time, PowerEventKind Kind)>();

        try
        {
            using var systemLog = new EventLog("System");

            foreach (EventLogEntry entry in systemLog.Entries)
            {
                if (entry.TimeGenerated < cutoffDate)
                    continue;

                int id = (int)(entry.InstanceId & 0xFFFF);
                string source = entry.Source ?? "";
                events.Add((entry.TimeGenerated, ClassifyEvent(source, id)));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EventLog import warning: {ex.Message}");
        }

        return BuildDailyUsage(events, DateTime.Now);
    }

    /// <summary>
    /// Sınıflandırılmış güç olaylarından günlük Aktif / Uyku sürelerini üretir.
    /// </summary>
    public static List<DailyUsage> BuildDailyUsage(IEnumerable<(DateTime Time, PowerEventKind Kind)> events, DateTime now)
    {
        var dailyMap = new Dictionary<string, DailyUsage>();
        var ordered = events.OrderBy(e => e.Time).ToList();

        var rawSegments = new List<StateSegment>();
        PowerState currentState = PowerState.Off;
        DateTime? stateStart = null;
        bool stateStartedByBoot = false;
        DateTime? lastEventTime = null;

        void CloseSegment(DateTime end)
        {
            if (stateStart.HasValue && currentState != PowerState.Off && end > stateStart.Value)
            {
                rawSegments.Add(new StateSegment
                {
                    Start = stateStart.Value,
                    End = end,
                    State = currentState
                });
            }
        }

        foreach (var ev in ordered)
        {
            switch (ev.Kind)
            {
                case PowerEventKind.Boot:
                    if (currentState == PowerState.Active && stateStartedByBoot && stateStart.HasValue &&
                        ev.Time - stateStart.Value < SameBootWindow)
                    {
                        // Aynı açılışın ikinci kaydı (12 ve 6005): yok say
                        break;
                    }

                    if (currentState != PowerState.Off)
                    {
                        // Temiz kapanış kaydı olmadan yeni açılış = beklenmedik kapanış (çökme, elektrik
                        // kesintisi, pil bitmesi). Kapanış anı bilinmediği için önceki oturum, açılıştan
                        // önceki son sistem kaydında bitirilir. (Kernel-Power 41 ve EventLog 6008 kayıtları
                        // çökme anında değil, sonraki açılışta yazıldığı için kapanış anı olarak kullanılamaz.)
                        CloseSegment(lastEventTime ?? stateStart ?? ev.Time);
                    }

                    currentState = PowerState.Active;
                    stateStart = ev.Time;
                    stateStartedByBoot = true;
                    break;

                case PowerEventKind.Wake:
                    if (currentState != PowerState.Active)
                    {
                        CloseSegment(ev.Time);
                        currentState = PowerState.Active;
                        stateStart = ev.Time;
                        stateStartedByBoot = false;
                    }
                    break;

                case PowerEventKind.Sleep:
                    if (currentState != PowerState.Sleep)
                    {
                        CloseSegment(ev.Time);
                        currentState = PowerState.Sleep;
                        stateStart = ev.Time;
                        stateStartedByBoot = false;
                    }
                    break;

                case PowerEventKind.Shutdown:
                    if (currentState != PowerState.Off)
                    {
                        CloseSegment(ev.Time);
                        currentState = PowerState.Off;
                        stateStart = ev.Time;
                        stateStartedByBoot = false;
                    }
                    break;
            }

            lastEventTime = ev.Time;
        }

        CloseSegment(now);

        // Modern Standby sırasında gerçekleşen kısa (< 120 sn) bakım uyanmalarını "Sleep" olarak birleştir
        for (int i = 0; i < rawSegments.Count; i++)
        {
            var seg = rawSegments[i];
            if (seg.State == PowerState.Active && seg.DurationSeconds < 120)
            {
                bool prevIsSleep = i > 0 && rawSegments[i - 1].State == PowerState.Sleep;
                bool nextIsSleep = i + 1 < rawSegments.Count && rawSegments[i + 1].State == PowerState.Sleep;
                if (prevIsSleep || nextIsSleep)
                {
                    seg.State = PowerState.Sleep;
                }
            }
        }

        foreach (var seg in rawSegments)
        {
            if (seg.State == PowerState.Active)
            {
                AddSegmentAcrossDays(dailyMap, seg.Start, seg.End, isSleep: false);
            }
            else if (seg.State == PowerState.Sleep)
            {
                AddSegmentAcrossDays(dailyMap, seg.Start, seg.End, isSleep: true);
            }
        }

        return dailyMap.Values.OrderByDescending(d => d.DateKey).ToList();
    }

    public static PowerEventKind ClassifyEvent(string source, int id)
    {
        if (source.Equals("EventLog", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 6005) return PowerEventKind.Boot;
            if (id == 6006) return PowerEventKind.Shutdown;
            // 6008 ("önceki kapanış beklenmedikti") açılışta yazılır: kapanış anı değildir
        }
        else if (source.Equals("Microsoft-Windows-Kernel-General", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 12) return PowerEventKind.Boot;
            if (id == 13) return PowerEventKind.Shutdown;
        }
        else if (source.Equals("Microsoft-Windows-Power-Troubleshooter", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 1) return PowerEventKind.Wake;
        }
        else if (source.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase))
        {
            if (id is 107 or 507) return PowerEventKind.Wake;
            if (id is 42 or 506) return PowerEventKind.Sleep;
            // 41 ("sistem düzgün kapatılmadan yeniden başlatıldı") açılışta yazılır: kapanış anı değildir
        }

        return PowerEventKind.Other;
    }

    private static void AddSegmentAcrossDays(Dictionary<string, DailyUsage> map, DateTime start, DateTime end, bool isSleep)
    {
        DateTime cursor = start;
        while (cursor < end)
        {
            DateTime nextMidnight = cursor.Date.AddDays(1);
            DateTime segmentEnd = end < nextMidnight ? end : nextMidnight;
            long seconds = (long)Math.Max(0, (segmentEnd - cursor).TotalSeconds);

            if (seconds > 0)
            {
                string key = cursor.ToString("yyyy-MM-dd");
                if (!map.TryGetValue(key, out var usage))
                {
                    usage = new DailyUsage
                    {
                        DateKey = key,
                        IsImportedFromEventLog = true
                    };
                    map[key] = usage;
                }

                if (isSleep)
                {
                    usage.SleepSeconds += seconds;
                }
                else
                {
                    usage.ActiveSeconds += seconds;
                    if (!usage.FirstSeen.HasValue || cursor < usage.FirstSeen.Value)
                        usage.FirstSeen = cursor;
                    if (!usage.LastSeen.HasValue || segmentEnd > usage.LastSeen.Value)
                        usage.LastSeen = segmentEnd;
                }
            }

            cursor = segmentEnd;
        }
    }
}
