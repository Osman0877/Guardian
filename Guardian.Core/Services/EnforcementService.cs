using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface IEnforcementService
    {
        bool IsEnforcementActive { get; }
        bool IsRunningAsWindowsAdmin { get; }
        bool IsWatchdogRunning { get; }

        event Action<string, string>? OnApplicationBlocked; // processName, displayName
        event Action<string, string, int>? OnAppLimitExceeded; // processName, displayName, limitMinutes
        event Action? OnDeviceLockTriggered;

        void Start();
        void Stop();
        void RefreshRules();
        void EnsureWatchdogRunning();
        void KillWatchdogForTesting();
    }

    public class EnforcementService : IEnforcementService, IDisposable
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usageTracker;
        private Dictionary<string, AppRule> _activeRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _rulesLock = new object();

        private CancellationTokenSource? _cts;
        private Task? _enforcementTask;
        private bool _wasDeviceLockedLastCheck = false;

        public bool IsEnforcementActive => _enforcementTask != null && !_enforcementTask.IsCompleted;
        public bool IsRunningAsWindowsAdmin { get; private set; }
        public bool IsWatchdogRunning { get; private set; } = false;

        public event Action<string, string>? OnApplicationBlocked;
        public event Action<string, string, int>? OnAppLimitExceeded;
        public event Action? OnDeviceLockTriggered;

        public EnforcementService(IDatabaseService db, IUsageTrackingService usageTracker)
        {
            _db = db;
            _usageTracker = usageTracker;
            CheckAdminStatus();
            RefreshRules();
        }

        private void CheckAdminStatus()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                IsRunningAsWindowsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                IsRunningAsWindowsAdmin = false;
            }
        }

        public void Start()
        {
            if (_enforcementTask != null) return;

            _cts = new CancellationTokenSource();
            _enforcementTask = Task.Run(() => EnforcementLoop(_cts.Token));
            Logger.Info("Enforcement Service started.");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try { _enforcementTask?.Wait(2000); } catch { }
                _cts.Dispose();
                _cts = null;
                _enforcementTask = null;
            }
            Logger.Info("Enforcement Service stopped.");
        }

        public void RefreshRules()
        {
            lock (_rulesLock)
            {
                _activeRules.Clear();
                var rules = _db.GetAllAppRules();
                foreach (var r in rules)
                {
                    _activeRules[r.ProcessName.ToLowerInvariant()] = r;
                }
            }
            Logger.Info($"Loaded {_activeRules.Count} application enforcement rules.");
        }

        private async Task EnforcementLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. Device Level Lock Enforcement
                    bool isLocked = _usageTracker.IsDeviceLimitReached;
                    if (isLocked && !_wasDeviceLockedLastCheck)
                    {
                        _wasDeviceLockedLastCheck = true;
                        OnDeviceLockTriggered?.Invoke();
                    }
                    else if (!isLocked)
                    {
                        _wasDeviceLockedLastCheck = false;
                    }

                    // 2. Application Process Monitoring & Enforcement
                    EnforceRunningProcesses();

                    // 3. Watchdog Health & Mutual Recovery Check
                    CheckWatchdogStatus();
                }
                catch (Exception ex)
                {
                    Logger.Error("Error during enforcement loop", ex);
                }

                await Task.Delay(1200, token);
            }
        }

        private static readonly HashSet<string> EscapeToolProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "taskmgr", "cmd", "powershell", "pwsh", "wscript", "cscript", "regedit", "msconfig", "taskkill", "processhacker"
        };

        private void EnforceRunningProcesses()
        {
            Process[] processes = Process.GetProcesses();
            var dailyRecord = _usageTracker.CurrentDailyRecord;
            bool isDeviceLocked = _usageTracker.IsDeviceLimitReached;

            foreach (var proc in processes)
            {
                try
                {
                    string pName = proc.ProcessName.ToLowerInvariant();

                    // Security: Terminate escape tools (Task Manager, Command Prompt, PowerShell) while device is locked
                    if (isDeviceLocked && EscapeToolProcesses.Contains(pName))
                    {
                        TerminateProcessSafely(proc, pName, "ESCAPE_TOOL_TERMINATED", "System tools are blocked while device screen time is locked.");
                        continue;
                    }

                    AppRule? rule = null;

                    lock (_rulesLock)
                    {
                        if (!_activeRules.TryGetValue(pName, out rule))
                        {
                            continue;
                        }
                    }

                    if (rule == null) continue;

                    // Condition A: Explicitly Blocked App
                    if (rule.IsBlocked)
                    {
                        TerminateProcessSafely(proc, rule.DisplayName, "BLOCKED_APP_TERMINATED", "Application is permanently blocked by parental controls.");
                        OnApplicationBlocked?.Invoke(rule.ProcessName, rule.DisplayName);
                        continue;
                    }

                    // Condition B: Application Daily Time Limit Exceeded
                    if (rule.DailyLimitMinutes > 0)
                    {
                        long limitSeconds = rule.DailyLimitMinutes * 60L;
                        long usedSeconds = 0;

                        if (dailyRecord.AppUsages.TryGetValue(pName, out var appUsage))
                        {
                            usedSeconds = appUsage.SecondsUsed;
                        }

                        if (usedSeconds >= limitSeconds)
                        {
                            TerminateProcessSafely(proc, rule.DisplayName, "APP_LIMIT_EXCEEDED", $"Daily time limit ({rule.DailyLimitMinutes}m) reached.");
                            OnAppLimitExceeded?.Invoke(rule.ProcessName, rule.DisplayName, rule.DailyLimitMinutes);
                        }
                    }
                }
                catch
                {
                    // Process may have already exited or system process
                }
            }
        }

        private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "winlogon", "services", "lsass", "smss", "svchost",
            "taskmgr", "system", "idle", "guardianwpf", "guardianwatchdog", "conhost", "fontdrvhost"
        };

        private void TerminateProcessSafely(Process proc, string displayName, string eventType, string reason)
        {
            try
            {
                string pName = proc.ProcessName.ToLowerInvariant();
                if (ProtectedSystemProcesses.Contains(pName))
                {
                    Logger.Warn($"Enforcement Safety Check prevented termination of critical system process: {pName}");
                    return;
                }

                if (!proc.HasExited)
                {
                    proc.Kill(true); // Terminate process and child tree
                    _db.LogIncident(eventType, proc.ProcessName, $"{displayName} was closed: {reason}");
                    Logger.Warn($"Enforcement action: Terminated {proc.ProcessName} ({displayName}). Reason: {reason}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to terminate restricted process {proc.ProcessName}", ex);
            }
        }

        private void CheckWatchdogStatus()
        {
            try
            {
                Process[] watchdogs = Process.GetProcessesByName("GuardianWatchdog");
                IsWatchdogRunning = (watchdogs.Length > 0);

                if (!IsWatchdogRunning)
                {
                    EnsureWatchdogRunning();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error checking watchdog status", ex);
            }
        }

        public void EnsureWatchdogRunning()
        {
            try
            {
                Process[] existing = Process.GetProcessesByName("GuardianWatchdog");
                if (existing.Length > 0)
                {
                    IsWatchdogRunning = true;
                    return;
                }

                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string watchdogExe = Path.Combine(currentDir, "GuardianWatchdog.exe");

                // In debug or test environments, check solution directory
                if (!File.Exists(watchdogExe))
                {
                    string fallback = Path.GetFullPath(Path.Combine(currentDir, "..\\..\\..\\..\\GuardianWatchdog\\bin\\Debug\\net10.0\\GuardianWatchdog.exe"));
                    if (File.Exists(fallback)) watchdogExe = fallback;
                }

                if (File.Exists(watchdogExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = watchdogExe,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                    IsWatchdogRunning = true;
                    Logger.Info("Spawned GuardianWatchdog.exe process.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error starting GuardianWatchdog.exe", ex);
            }
        }

        public void KillWatchdogForTesting()
        {
            try
            {
                Process[] watchdogs = Process.GetProcessesByName("GuardianWatchdog");
                foreach (var p in watchdogs)
                {
                    try { p.Kill(); } catch { }
                }
                IsWatchdogRunning = false;
                Logger.Info("Killed GuardianWatchdog processes for recovery test.");
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
