using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Guardian.Core.Models;
using Guardian.Core.Win32;

namespace Guardian.Core.Services
{
    public interface IUsageTrackingService
    {
        DailyUsageRecord CurrentDailyRecord { get; }
        AdvancedDaySchedule CurrentSchedule { get; }
        string ActiveChildId { get; }
        string ActiveProcessName { get; }
        string ActiveWindowTitle { get; }
        bool IsUserIdle { get; }
        long TotalDeviceSecondsToday { get; }
        int DailyLimitMinutesToday { get; }
        long RemainingDeviceSecondsToday { get; }
        bool IsDeviceLimitReached { get; }
        bool IsBedtimeRestricted { get; }
        bool IsOutsideAllowedHours { get; }
        string CurrentRestrictionReason { get; }

        // AFK Mode
        bool IsAfkModeActive { get; }
        DateTime? AfkStartedAt { get; }
        int AfkDurationMinutes { get; }
        TimeSpan AfkTimeRemaining { get; }
        long TotalAfkSecondsToday { get; }

        event Action? OnUsageUpdated;
        event Action? OnDeviceLimitReached;
        event Action? OnDateChanged;
        event Action? OnAfkStateChanged;

        void Start();
        void Stop();
        void SetActiveChild(string childId);
        void GrantBonusMinutes(int minutes);
        void UnlockDeviceForToday();
        void LockDeviceManually(string reason = "Device locked by parent");
        bool ActivateAfkMode(string password, int durationMinutes = 0);
        bool DeactivateAfkMode(string password);
        void ForceDeactivateAfkMode(string reason = "AFK Mode ended");
        void RefreshSchedule();
        void FlushToDatabase();
    }

    public class UsageTrackingService : IUsageTrackingService, IDisposable
    {
        private readonly IDatabaseService _db;
        private AdvancedDaySchedule _schedule;
        private DailyUsageRecord _currentRecord;
        private AfkState _afkState;
        private string _activeChildId = "default";
        private readonly object _stateLock = new object();

        private CancellationTokenSource? _cts;
        private Task? _trackingTask;
        private long _lastTickCount;
        private DateTime _lastFlushTime = DateTime.MinValue;

        private bool _notified30m = false;
        private bool _notified15m = false;

        private const int IdleThresholdMs = 180000; // 3 minutes idle threshold

        public DailyUsageRecord CurrentDailyRecord { get { lock (_stateLock) return _currentRecord; } }
        public AdvancedDaySchedule CurrentSchedule { get { lock (_stateLock) return _schedule; } }
        public string ActiveChildId => _activeChildId;

        public string ActiveProcessName { get; private set; } = string.Empty;
        public string ActiveWindowTitle { get; private set; } = string.Empty;
        public bool IsUserIdle { get; private set; } = false;

        // AFK Mode Properties
        public bool IsAfkModeActive { get { lock (_stateLock) return _afkState.IsAfkModeActive; } }
        public DateTime? AfkStartedAt { get { lock (_stateLock) return _afkState.AfkStartedAt; } }
        public int AfkDurationMinutes { get { lock (_stateLock) return _afkState.AfkDurationMinutes; } }
        public TimeSpan AfkTimeRemaining
        {
            get
            {
                lock (_stateLock)
                {
                    if (!_afkState.IsAfkModeActive || !_afkState.AfkStartedAt.HasValue) return TimeSpan.Zero;
                    if (_afkState.AfkDurationMinutes <= 0) return TimeSpan.MaxValue; // Until manually disabled

                    DateTime exp = _afkState.AfkStartedAt.Value.AddMinutes(_afkState.AfkDurationMinutes);
                    TimeSpan rem = exp - DateTime.Now;
                    return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
                }
            }
        }
        public long TotalAfkSecondsToday { get { lock (_stateLock) return _currentRecord.TotalAfkSecondsUsed; } }

        public long TotalDeviceSecondsToday
        {
            get { lock (_stateLock) return _currentRecord.TotalDeviceSecondsUsed; }
        }

        public int DailyLimitMinutesToday
        {
            get
            {
                lock (_stateLock)
                {
                    return _schedule.LimitMinutes;
                }
            }
        }

        public long RemainingDeviceSecondsToday
        {
            get
            {
                lock (_stateLock)
                {
                    long totalAllowedSecs = (DailyLimitMinutesToday * 60L) + _currentRecord.ManualBonusSeconds;
                    return Math.Max(0, totalAllowedSecs - _currentRecord.TotalDeviceSecondsUsed);
                }
            }
        }

        public bool IsBedtimeRestricted
        {
            get
            {
                lock (_stateLock)
                {
                    if (!_schedule.BedtimeEnabled) return false;
                    TimeSpan nowTime = DateTime.Now.TimeOfDay;
                    return nowTime >= _schedule.Bedtime;
                }
            }
        }

        public bool IsOutsideAllowedHours
        {
            get
            {
                lock (_stateLock)
                {
                    if (!_schedule.HasAllowedHours) return false;
                    TimeSpan nowTime = DateTime.Now.TimeOfDay;
                    return nowTime < _schedule.AllowedStartTime || nowTime > _schedule.AllowedEndTime;
                }
            }
        }

        public bool IsDeviceLimitReached
        {
            get
            {
                lock (_stateLock)
                {
                    if (_currentRecord.ParentUnlockedForToday) return false;
                    if (_currentRecord.IsDeviceLocked) return true;
                    long totalAllowedSecs = (DailyLimitMinutesToday * 60L) + _currentRecord.ManualBonusSeconds;
                    return _currentRecord.TotalDeviceSecondsUsed >= totalAllowedSecs || IsBedtimeRestricted || IsOutsideAllowedHours;
                }
            }
        }

        public string CurrentRestrictionReason
        {
            get
            {
                lock (_stateLock)
                {
                    if (!string.IsNullOrEmpty(_currentRecord.LockReason)) return _currentRecord.LockReason;
                    if (IsBedtimeRestricted) return $"Bedtime restriction reached ({_schedule.Bedtime:hh\\:mm}). Time to sleep!";
                    if (IsOutsideAllowedHours) return $"Outside allowed hours ({_schedule.AllowedStartTime:hh\\:mm} - {_schedule.AllowedEndTime:hh\\:mm}).";
                    if (_currentRecord.TotalDeviceSecondsUsed >= (DailyLimitMinutesToday * 60L) + _currentRecord.ManualBonusSeconds)
                        return $"Daily screen-time limit of {DailyLimitMinutesToday / 60}h {DailyLimitMinutesToday % 60:D2}m has been reached.";
                    return "Device Locked";
                }
            }
        }

        public event Action? OnUsageUpdated;
        public event Action? OnDeviceLimitReached;
        public event Action? OnDateChanged;
        public event Action? OnAfkStateChanged;

        public UsageTrackingService(IDatabaseService db)
        {
            _db = db;
            var settings = _db.LoadSettings();
            _activeChildId = string.IsNullOrEmpty(settings.ActiveChildId) ? "default" : settings.ActiveChildId;

            _schedule = _db.GetAdvancedSchedule(DateTime.Today.DayOfWeek);
            _currentRecord = _db.GetOrCreateDailyUsage(DateTime.Today, _activeChildId);
            _afkState = _db.LoadAfkState();
            _lastTickCount = Environment.TickCount64;

            // Restart Recovery: Check if AFK duration expired during reboot and handle AFK session recording
            var activeSession = _db.GetActiveAfkSession();
            if (_afkState.IsAfkModeActive)
            {
                if (_afkState.AfkStartedAt.HasValue && _afkState.AfkDurationMinutes > 0)
                {
                    DateTime exp = _afkState.AfkStartedAt.Value.AddMinutes(_afkState.AfkDurationMinutes);
                    if (DateTime.Now >= exp)
                    {
                        if (activeSession != null)
                        {
                            DateTime expUtc = _afkState.AfkStartedAt.Value.ToUniversalTime().AddMinutes(_afkState.AfkDurationMinutes);
                            activeSession.EndTimestamp = expUtc;
                            activeSession.DurationSeconds = (long)(expUtc - activeSession.StartTimestamp).TotalSeconds;
                            activeSession.EndedBy = "System";
                            activeSession.EndReason = "Duration Expired";
                            _db.SaveAfkSession(activeSession);
                        }
                        ForceDeactivateAfkMode("AFK Mode duration expired during system restart");
                    }
                    else if (activeSession == null && _afkState.AfkStartedAt.HasValue)
                    {
                        _db.SaveAfkSession(new AfkSessionRecord
                        {
                            Id = Guid.NewGuid().ToString(),
                            StartTimestamp = _afkState.AfkStartedAt.Value.ToUniversalTime(),
                            EndTimestamp = null,
                            ActivationMethod = "Parent Password",
                            ActivatedBy = "Parent",
                            EndReason = "Active",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else if (activeSession == null && _afkState.AfkStartedAt.HasValue)
                {
                    _db.SaveAfkSession(new AfkSessionRecord
                    {
                        Id = Guid.NewGuid().ToString(),
                        StartTimestamp = _afkState.AfkStartedAt.Value.ToUniversalTime(),
                        EndTimestamp = null,
                        ActivationMethod = "Parent Password",
                        ActivatedBy = "Parent",
                        EndReason = "Active",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (activeSession != null)
            {
                DateTime nowUtc = DateTime.UtcNow;
                activeSession.EndTimestamp = nowUtc;
                activeSession.DurationSeconds = (long)(nowUtc - activeSession.StartTimestamp).TotalSeconds;
                activeSession.EndedBy = "System";
                activeSession.EndReason = "System Shutdown";
                _db.SaveAfkSession(activeSession);
            }
        }

        public void Start()
        {
            if (_trackingTask != null) return;

            _cts = new CancellationTokenSource();
            _lastTickCount = Environment.TickCount64;
            _trackingTask = Task.Run(() => TrackingLoop(_cts.Token));
            Logger.Info("Authoritative Usage Tracking Service started.");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try { _trackingTask?.Wait(2000); } catch { }
                _cts.Dispose();
                _cts = null;
                _trackingTask = null;
            }

            FlushToDatabase();
            Logger.Info("Authoritative Usage Tracking Service stopped.");
        }

        public void SetActiveChild(string childId)
        {
            lock (_stateLock)
            {
                FlushToDatabase();
                _activeChildId = childId;
                _currentRecord = _db.GetOrCreateDailyUsage(DateTime.Today, _activeChildId);

                var s = _db.LoadSettings();
                s.ActiveChildId = childId;
                _db.SaveSettings(s);
            }
            RefreshSchedule();
        }

        public void RefreshSchedule()
        {
            lock (_stateLock)
            {
                _schedule = _db.GetAdvancedSchedule(DateTime.Today.DayOfWeek);
            }
            OnUsageUpdated?.Invoke();
        }

        public void GrantBonusMinutes(int minutes)
        {
            lock (_stateLock)
            {
                _currentRecord.ManualBonusSeconds += (minutes * 60L);
                _currentRecord.IsDeviceLocked = false;
                _currentRecord.LockReason = "";
                _db.SaveDailyUsage(_currentRecord);
            }

            _db.SaveNotification(new GuardianNotification
            {
                Title = "Bonus Screen Time Granted",
                Message = $"Parent granted +{minutes} minutes of extra screen time.",
                Severity = NotificationSeverity.Success
            });

            Logger.Info($"Granted {minutes} bonus minutes to today's screen time allowance.");
            OnUsageUpdated?.Invoke();
        }

        public void UnlockDeviceForToday()
        {
            lock (_stateLock)
            {
                _currentRecord.ParentUnlockedForToday = true;
                _currentRecord.IsDeviceLocked = false;
                _currentRecord.LockReason = "";
                _currentRecord.LockDate = DateTime.Today.ToString("yyyy-MM-dd");
                _db.SaveDailyUsage(_currentRecord);
            }

            _db.SaveNotification(new GuardianNotification
            {
                Title = "Device Unlocked by Parent",
                Message = "Parent unlocked the device for the remainder of today.",
                Severity = NotificationSeverity.Success
            });

            _db.LogIncident("DEVICE_UNLOCKED_PARENT", "ParentAuth", "Parent authenticated and unlocked device for today.");
            Logger.Info("Parent unlocked device for today.");
            OnUsageUpdated?.Invoke();
        }

        public void LockDeviceManually(string reason = "Device locked by parent")
        {
            lock (_stateLock)
            {
                _currentRecord.IsDeviceLocked = true;
                _currentRecord.ParentUnlockedForToday = false;
                _currentRecord.LockReason = reason;
                _db.SaveDailyUsage(_currentRecord);
            }

            _db.SaveNotification(new GuardianNotification
            {
                Title = "Device Locked",
                Message = reason,
                Severity = NotificationSeverity.Alert
            });

            _db.LogIncident("DEVICE_LOCKED_MANUAL", "Parent", reason);
            Logger.Info($"Device manually locked: {reason}");
            OnUsageUpdated?.Invoke();
            OnDeviceLimitReached?.Invoke();
        }

        public bool ActivateAfkMode(string password, int durationMinutes = 0)
        {
            var auth = new AuthenticationService(_db);
            if (!auth.VerifyPassword(password) && !auth.HasAuthenticatedSession)
            {
                Logger.Warn("Failed parent authentication attempt to activate AFK Mode.");
                return false;
            }

            lock (_stateLock)
            {
                DateTime nowLocal = DateTime.Now;
                DateTime nowUtc = DateTime.UtcNow;

                _afkState.IsAfkModeActive = true;
                _afkState.AfkStartedAt = nowLocal;
                _afkState.AfkEndedAt = null;
                _afkState.AfkDurationMinutes = durationMinutes;
                _afkState.ActivatedBy = "Parent";

                _db.SaveAfkState(_afkState);

                // Create new active AFK session record in SQLite database
                var session = new AfkSessionRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    StartTimestamp = nowUtc,
                    EndTimestamp = null,
                    DurationSeconds = 0,
                    ActivationMethod = "Parent Password",
                    ActivatedBy = "Parent",
                    EndedBy = "Pending",
                    EndReason = "Active",
                    CreatedAt = nowUtc
                };
                _db.SaveAfkSession(session);
            }

            string durText = durationMinutes > 0 ? $"{durationMinutes} minutes" : "Until manually disabled";
            _db.LogIncident("AFK_MODE_ACTIVATED", "Parent", $"Parent activated AFK Mode (Duration: {durText}). Screen time accounting paused.");
            _db.SaveNotification(new GuardianNotification
            {
                Title = "AFK Mode Activated",
                Message = $"Parent activated AFK Mode ({durText}). Screen-time accounting is paused.",
                Severity = NotificationSeverity.Success
            });

            Logger.Info($"AFK Mode activated ({durText}).");
            OnAfkStateChanged?.Invoke();
            OnUsageUpdated?.Invoke();
            return true;
        }

        public bool DeactivateAfkMode(string password)
        {
            var auth = new AuthenticationService(_db);
            if (!auth.VerifyPassword(password) && !auth.HasAuthenticatedSession)
            {
                Logger.Warn("Failed parent authentication attempt to deactivate AFK Mode.");
                return false;
            }

            ForceDeactivateAfkMode("Parent deactivated AFK Mode");
            return true;
        }

        public void ForceDeactivateAfkMode(string reason = "AFK Mode ended")
        {
            DateTime now = DateTime.Now;
            TimeSpan afkDuration = TimeSpan.Zero;

            lock (_stateLock)
            {
                if (!_afkState.IsAfkModeActive) return;

                if (_afkState.AfkStartedAt.HasValue)
                {
                    afkDuration = now - _afkState.AfkStartedAt.Value;
                }

                _afkState.IsAfkModeActive = false;
                _afkState.AfkEndedAt = now;
                _db.SaveAfkState(_afkState);

                // Close active AFK Session record in SQLite database
                var activeSession = _db.GetActiveAfkSession();
                if (activeSession != null)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    activeSession.EndTimestamp = nowUtc;
                    activeSession.DurationSeconds = (long)(nowUtc - activeSession.StartTimestamp).TotalSeconds;
                    activeSession.EndedBy = reason.Contains("Parent") ? "Parent" : "System";
                    activeSession.EndReason = reason.Contains("Parent") ? "Parent Disabled" : (reason.Contains("expired") ? "Duration Expired" : reason);
                    _db.SaveAfkSession(activeSession);
                }
            }

            string durFormatted = $"{(int)afkDuration.TotalHours}h {afkDuration.Minutes:D2}m";
            _db.LogIncident("AFK_MODE_DEACTIVATED", "System", $"{reason} (Duration: {durFormatted}, Excluded: {durFormatted}).");
            _db.SaveNotification(new GuardianNotification
            {
                Title = "AFK Mode Deactivated",
                Message = $"AFK Mode ended ({durFormatted} screen time excluded). Normal accounting resumed.",
                Severity = NotificationSeverity.Info
            });

            Logger.Info($"AFK Mode deactivated: {reason}. Duration: {durFormatted}");
            OnAfkStateChanged?.Invoke();
            OnUsageUpdated?.Invoke();
        }

        public void FlushToDatabase()
        {
            lock (_stateLock)
            {
                _db.SaveDailyUsage(_currentRecord);
                _lastFlushTime = DateTime.Now;
            }
        }

        private async Task TrackingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long currentTick = Environment.TickCount64;
                    long deltaMs = currentTick - _lastTickCount;
                    _lastTickCount = currentTick;

                    if (deltaMs < 0 || deltaMs > 10000)
                    {
                        deltaMs = 1000;
                    }

                    float deltaSeconds = deltaMs / 1000f;

                    // 1. Date Boundary
                    CheckDateBoundary();

                    // 2. AFK Mode Expiration Check
                    if (IsAfkModeActive && AfkStartedAt.HasValue && AfkDurationMinutes > 0)
                    {
                        DateTime exp = AfkStartedAt.Value.AddMinutes(AfkDurationMinutes);
                        if (DateTime.Now >= exp)
                        {
                            ForceDeactivateAfkMode("AFK Mode duration automatically expired");
                        }
                    }

                    // 3. Idle Check
                    uint idleMs = NativeMethods.GetIdleTimeMs();
                    IsUserIdle = (idleMs >= IdleThresholdMs);

                    // 4. Foreground Resolution
                    ResolveForegroundProcess();

                    // 5. Time Accumulation (Central Engine Exclusion during AFK)
                    if (!IsUserIdle && deltaSeconds > 0)
                    {
                        lock (_stateLock)
                        {
                            long secToAdd = (long)Math.Round(deltaSeconds);
                            if (secToAdd > 0)
                            {
                                if (IsAfkModeActive)
                                {
                                    // AFK Mode: Screen-time accounting is paused. Accumulate only AFK time.
                                    _currentRecord.TotalAfkSecondsUsed += secToAdd;
                                }
                                else
                                {
                                    // Normal Mode: Accumulate Screen Time and App Usage
                                    _currentRecord.TotalDeviceSecondsUsed += secToAdd;

                                    if (!string.IsNullOrEmpty(ActiveProcessName))
                                    {
                                        if (!_currentRecord.AppUsages.TryGetValue(ActiveProcessName, out var appEntry))
                                        {
                                            appEntry = new AppUsageEntry
                                            {
                                                ProcessName = ActiveProcessName,
                                                DisplayName = GetFriendlyProcessName(ActiveProcessName)
                                            };
                                            _currentRecord.AppUsages[ActiveProcessName] = appEntry;
                                        }

                                        appEntry.SecondsUsed += secToAdd;
                                        appEntry.LastActive = DateTime.Now;
                                    }
                                }
                            }
                        }

                        // Check Threshold Notifications
                        long remSecs = RemainingDeviceSecondsToday;
                        if (remSecs <= 1800 && remSecs > 900 && !_notified30m)
                        {
                            _notified30m = true;
                            _db.SaveNotification(new GuardianNotification
                            {
                                Title = "30 Minutes Remaining",
                                Message = "You have 30 minutes of screen time left today.",
                                Severity = NotificationSeverity.Warning
                            });
                        }
                        else if (remSecs <= 900 && remSecs > 0 && !_notified15m)
                        {
                            _notified15m = true;
                            _db.SaveNotification(new GuardianNotification
                            {
                                Title = "15 Minutes Remaining",
                                Message = "You have 15 minutes of screen time left today. Wrap up your activities!",
                                Severity = NotificationSeverity.Alert
                            });
                        }

                        // Check if device limit reached
                        if (IsDeviceLimitReached && !_currentRecord.IsDeviceLocked)
                        {
                            lock (_stateLock)
                            {
                                _currentRecord.IsDeviceLocked = true;
                                _currentRecord.LockReason = CurrentRestrictionReason;
                            }

                            _db.SaveNotification(new GuardianNotification
                            {
                                Title = "Device Locked",
                                Message = CurrentRestrictionReason,
                                Severity = NotificationSeverity.Alert
                            });

                            _db.LogIncident("DEVICE_LOCKED", "System", CurrentRestrictionReason);
                            OnDeviceLimitReached?.Invoke();
                        }
                    }

                    // Periodic DB flush every 5 seconds
                    if ((DateTime.Now - _lastFlushTime).TotalSeconds >= 5)
                    {
                        FlushToDatabase();
                    }

                    OnUsageUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.Error("Error in Usage Tracking loop", ex);
                }

                await Task.Delay(1000, token);
            }
        }

        private void CheckDateBoundary()
        {
            string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            lock (_stateLock)
            {
                if (_currentRecord.DateString != todayStr)
                {
                    Logger.Info($"Midnight date boundary reached: {_currentRecord.DateString} -> {todayStr}.");
                    _db.SaveDailyUsage(_currentRecord);

                    _currentRecord = _db.GetOrCreateDailyUsage(DateTime.Today, _activeChildId);
                    _schedule = _db.GetAdvancedSchedule(DateTime.Today.DayOfWeek);
                    _notified30m = false;
                    _notified15m = false;
                    OnDateChanged?.Invoke();
                }
            }
        }

        private void ResolveForegroundProcess()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    ActiveProcessName = string.Empty;
                    ActiveWindowTitle = string.Empty;
                    return;
                }

                ActiveWindowTitle = NativeMethods.GetActiveWindowTitle();

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0) return;

                IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        uint size = (uint)sb.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                        {
                            string fullPath = sb.ToString();
                            ActiveProcessName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                            return;
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }

                try
                {
                    using var proc = Process.GetProcessById((int)processId);
                    ActiveProcessName = proc.ProcessName.ToLowerInvariant();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Error("Error resolving foreground process", ex);
            }
        }

        private static string GetFriendlyProcessName(string processName)
        {
            return processName.ToLowerInvariant() switch
            {
                "robloxplayerbeta" => "Roblox",
                "chrome" => "Google Chrome",
                "msedge" => "Microsoft Edge",
                "discord" => "Discord",
                "steam" => "Steam",
                "minecraft.windows" => "Minecraft",
                "fortniteclient-win64-shipping" => "Fortnite",
                "spotify" => "Spotify",
                "cmd" => "Command Prompt",
                "powershell" => "PowerShell",
                _ => char.ToUpper(processName[0]) + processName.Substring(1)
            };
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
