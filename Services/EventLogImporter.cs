using System.Diagnostics;
using WindowScreenTime.Models;

namespace WindowScreenTime.Services;

public static class EventLogImporter
{
    private enum PowerState
    {
        Off,
        Active,
        Sleep
    }

    private sealed class StateTransition
    {
        public DateTime Time { get; init; }
        public PowerState TargetState { get; init; }
    }

    private sealed class StateSegment
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public PowerState State { get; set; }
        public double DurationSeconds => (End - Start).TotalSeconds;
    }

    /// <summary>
    /// Son N gün için Windows System Event Log kayıtlarını (Modern Standby 506/507 dahil) tarayarak
    /// Aktif ve Uykuda geçen süreleri hesaplar.
    /// </summary>
    public static List<DailyUsage> ImportRecentDaysFromSystemLog(int maxDaysBack = 30)
    {
        var dailyMap = new Dictionary<string, DailyUsage>();
        DateTime cutoffDate = DateTime.Today.AddDays(-maxDaysBack);

        try
        {
            using var systemLog = new EventLog("System");
            var transitions = new List<StateTransition>();

            foreach (EventLogEntry entry in systemLog.Entries)
            {
                if (entry.TimeGenerated < cutoffDate)
                    continue;

                int id = (int)(entry.InstanceId & 0xFFFF);
                string source = entry.Source ?? "";

                PowerState? state = ClassifyEvent(source, id);
                if (state.HasValue)
                {
                    transitions.Add(new StateTransition
                    {
                        Time = entry.TimeGenerated,
                        TargetState = state.Value
                    });
                }
            }

            transitions.Sort((a, b) => a.Time.CompareTo(b.Time));

            // Ardışık durum segmentlerini oluştur
            var rawSegments = new List<StateSegment>();
            PowerState currentState = PowerState.Off;
            DateTime? stateStart = null;

            foreach (var tr in transitions)
            {
                if (!stateStart.HasValue)
                {
                    currentState = tr.TargetState;
                    stateStart = tr.Time;
                    continue;
                }

                if (tr.TargetState != currentState)
                {
                    if (tr.Time > stateStart.Value)
                    {
                        rawSegments.Add(new StateSegment
                        {
                            Start = stateStart.Value,
                            End = tr.Time,
                            State = currentState
                        });
                    }
                    currentState = tr.TargetState;
                    stateStart = tr.Time;
                }
            }

            DateTime now = DateTime.Now;
            if (stateStart.HasValue && now > stateStart.Value && currentState != PowerState.Off)
            {
                rawSegments.Add(new StateSegment
                {
                    Start = stateStart.Value,
                    End = now,
                    State = currentState
                });
            }

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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EventLog import warning: {ex.Message}");
        }

        return dailyMap.Values.OrderByDescending(d => d.DateKey).ToList();
    }

    private static PowerState? ClassifyEvent(string source, int id)
    {
        if (source.Equals("EventLog", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 6005) return PowerState.Active;
            if (id is 6006 or 6008) return PowerState.Off;
        }
        else if (source.Equals("Microsoft-Windows-Kernel-General", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 12) return PowerState.Active;
            if (id == 13) return PowerState.Off;
        }
        else if (source.Equals("Microsoft-Windows-Power-Troubleshooter", StringComparison.OrdinalIgnoreCase))
        {
            if (id == 1) return PowerState.Active;
        }
        else if (source.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase))
        {
            if (id is 107 or 507) return PowerState.Active;
            if (id is 42 or 506) return PowerState.Sleep;
            if (id == 41) return PowerState.Off;
        }

        return null;
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
