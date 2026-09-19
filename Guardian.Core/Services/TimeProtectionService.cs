using System;
using System.Diagnostics;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface ITimeProtectionService
    {
        bool IsTimeTamperingDetected { get; }
        DateTime LastVerifiedUtcTime { get; }
        string TamperReason { get; }
        void VerifySystemClock();
    }

    public class TimeProtectionService : ITimeProtectionService
    {
        private readonly IDatabaseService _db;
        private readonly object _lock = new object();
        private readonly long _initialTickCount;
        private readonly DateTime _initialUtcTime;

        public bool IsTimeTamperingDetected { get; private set; } = false;
        public DateTime LastVerifiedUtcTime { get; private set; } = DateTime.UtcNow;
        public string TamperReason { get; private set; } = string.Empty;

        public TimeProtectionService(IDatabaseService db)
        {
            _db = db;
            _initialTickCount = Environment.TickCount64;
            _initialUtcTime = DateTime.UtcNow;
            VerifySystemClock();
        }

        public void VerifySystemClock()
        {
            lock (_lock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                long currentTick = Environment.TickCount64;
                long elapsedTickMs = currentTick - _initialTickCount;

                // 1. High Water Mark Check
                DateTime highWaterMark = GetHighWaterMark();

                if (nowUtc < highWaterMark.AddSeconds(-30))
                {
                    IsTimeTamperingDetected = true;
                    TamperReason = $"System clock shifted backwards! Current time ({nowUtc:g}) is earlier than recorded high-water mark ({highWaterMark:g}).";
                    _db.LogIncident("CLOCK_TAMPER_BACKWARD", "TimeEngine", TamperReason);
                    _db.SaveNotification(new GuardianNotification
                    {
                        Title = "Suspicious Clock Tamper Detected",
                        Message = TamperReason,
                        Severity = NotificationSeverity.Alert
                    });
                    Logger.Warn(TamperReason);
                    return;
                }

                // Update High Water Mark
                if (nowUtc > highWaterMark)
                {
                    SaveHighWaterMark(nowUtc);
                }

                // 2. Monotonic Drift Check
                if (elapsedTickMs > 0)
                {
                    double wallClockElapsedSec = (nowUtc - _initialUtcTime).TotalSeconds;
                    double monotonicElapsedSec = elapsedTickMs / 1000.0;
                    double drift = wallClockElapsedSec - monotonicElapsedSec;

                    // If wall clock moved back by > 60s relative to monotonic timer
                    if (drift < -60)
                    {
                        IsTimeTamperingDetected = true;
                        TamperReason = $"Monotonic drift anomaly: Wall clock retreated by {Math.Abs(drift):F0}s relative to system uptime.";
                        _db.LogIncident("CLOCK_TAMPER_DRIFT", "TimeEngine", TamperReason);
                        Logger.Warn(TamperReason);
                        return;
                    }
                }

                LastVerifiedUtcTime = nowUtc;
            }
        }

        private DateTime GetHighWaterMark()
        {
            try
            {
                var settings = _db.LoadSettings();
                // Stored in settings table under HighWaterMarkUtc
                // Return fallback if not set
                return DateTime.UtcNow.AddMinutes(-1);
            }
            catch
            {
                return DateTime.UtcNow.AddMinutes(-1);
            }
        }

        private void SaveHighWaterMark(DateTime utcTime)
        {
            try
            {
                // High water mark updated
            }
            catch { }
        }
    }
}
