using System;
using System.Collections.Generic;

namespace Guardian.Core.Models
{
    public enum UserRole
    {
        NotSelected,
        Parent,
        Child
    }

    public enum RequestType
    {
        ExtraScreenTime,
        UnlockApp,
        WebAccess,
        GeneralRequest
    }

    public enum RequestStatus
    {
        Pending,
        Approved,
        Denied
    }

    public enum ProtectionLevel
    {
        Protected,          // Green: All services healthy & enforcing
        Attention,          // Yellow: Non-fatal (e.g. Admin Windows account, Bedtime approaching)
        ProtectionProblem   // Red: Critical component failure
    }

    public enum NotificationSeverity
    {
        Info,
        Warning,
        Alert,
        Success
    }

    public enum InvitationStatus
    {
        Pending,
        Accepted,
        Declined,
        Cancelled,
        Expired
    }

    public class ParentChildInvitation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ParentUserId { get; set; } = string.Empty;
        public string ParentUsername { get; set; } = string.Empty;
        public string ChildUserId { get; set; } = string.Empty;
        public string ChildUsername { get; set; } = string.Empty;
        public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; } = null;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    }

    public class ParentChildRelationship
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ParentUserId { get; set; } = string.Empty;
        public string ChildUserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    public class UserAccount
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = string.Empty;
        public string NormalizedUsername { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Parent;
        public string ParentId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    public class ChildProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ParentId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = "Child";
        public string Avatar { get; set; } = "👦";
        public int Age { get; set; } = 10;
        public bool IsActive { get; set; } = true;
        public int DailyLimitMinutes { get; set; } = 120;
        public TimeSpan Bedtime { get; set; } = new TimeSpan(21, 30, 0); // 9:30 PM default
        public bool BedtimeEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class AdvancedDaySchedule
    {
        public DayOfWeek Day { get; set; }
        public int LimitMinutes { get; set; } = 120;
        public bool HasAllowedHours { get; set; } = false;
        public TimeSpan AllowedStartTime { get; set; } = new TimeSpan(17, 0, 0); // 5:00 PM
        public TimeSpan AllowedEndTime { get; set; } = new TimeSpan(20, 0, 0);   // 8:00 PM
        public bool BedtimeEnabled { get; set; } = true;
        public TimeSpan Bedtime { get; set; } = new TimeSpan(21, 30, 0);         // 9:30 PM
    }

    public class WebFilterRule
    {
        public string Domain { get; set; } = string.Empty; // e.g. "tiktok.com"
        public string Category { get; set; } = "Social Media";
        public bool IsBlocked { get; set; } = true;
        public string Reason { get; set; } = "Parental Block";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class GuardianNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
        public bool IsRead { get; set; } = false;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class AppRule
    {
        public string ProcessName { get; set; } = string.Empty; // lowercase, e.g. "robloxplayerbeta"
        public string DisplayName { get; set; } = string.Empty; // e.g. "Roblox"
        public bool IsBlocked { get; set; } = false;
        public int DailyLimitMinutes { get; set; } = 0; // 0 = unlimited, >0 = limit
        public string Category { get; set; } = "Games";
    }

    public class AppUsageEntry
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long SecondsUsed { get; set; } = 0;
        public DateTime LastActive { get; set; } = DateTime.Now;
    }

    public class DailyUsageRecord
    {
        public string DateString { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public string ChildId { get; set; } = "default";
        public long TotalDeviceSecondsUsed { get; set; } = 0;
        public long TotalAfkSecondsUsed { get; set; } = 0;
        public long ManualBonusSeconds { get; set; } = 0;
        public bool IsDeviceLocked { get; set; } = false;
        public string LockReason { get; set; } = string.Empty;
        public string LockDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public bool ParentUnlockedForToday { get; set; } = false;
        public Dictionary<string, AppUsageEntry> AppUsages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class AfkState
    {
        public bool IsAfkModeActive { get; set; } = false;
        public DateTime? AfkStartedAt { get; set; } = null;
        public DateTime? AfkEndedAt { get; set; } = null;
        public int AfkDurationMinutes { get; set; } = 0; // 0 = Until manually disabled, 15, 30, 60, 120
        public string ActivatedBy { get; set; } = "Parent";
    }

    public class AfkSessionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTimestamp { get; set; } = DateTime.UtcNow;
        public DateTime? EndTimestamp { get; set; } = null;
        public long DurationSeconds { get; set; } = 0;
        public string ActivationMethod { get; set; } = "Parent Password";
        public string ActivatedBy { get; set; } = "Parent";
        public string EndedBy { get; set; } = "Pending";
        public string EndReason { get; set; } = "Active"; // Active, Parent Disabled, Duration Expired, System Shutdown, System Restart, Recovery
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Helpers for UI display
        public DateTime LocalStartTime => StartTimestamp.ToLocalTime();
        public DateTime? LocalEndTime => EndTimestamp?.ToLocalTime();
        public TimeSpan CalculatedDuration => EndTimestamp.HasValue
            ? (EndTimestamp.Value > StartTimestamp ? EndTimestamp.Value - StartTimestamp : TimeSpan.Zero)
            : (DateTime.UtcNow > StartTimestamp ? DateTime.UtcNow - StartTimestamp : TimeSpan.Zero);

        public string FormattedStartDate => LocalStartTime.ToString("MMM dd, yyyy");
        public string FormattedStartTime => LocalStartTime.ToString("h:mm:ss tt");
        public string FormattedEndDate => LocalEndTime.HasValue ? LocalEndTime.Value.ToString("MMM dd, yyyy") : "Active";
        public string FormattedEndTime => LocalEndTime.HasValue ? LocalEndTime.Value.ToString("h:mm:ss tt") : "In Progress";
        public string FormattedDuration
        {
            get
            {
                TimeSpan d = CalculatedDuration;
                if (d.TotalHours >= 1)
                    return $"{(int)d.TotalHours}h {d.Minutes:D2}m {d.Seconds:D2}s";
                if (d.TotalMinutes >= 1)
                    return $"{d.Minutes}m {d.Seconds:D2}s";
                return $"{d.Seconds}s";
            }
        }
    }

    public class AuditLogEntry
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Category { get; set; } = "System"; // Auth, ClockTamper, RuleChange, Watchdog, Security
        public string EventName { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string UserOrProcess { get; set; } = "System";
    }

    public class ChildRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ChildName { get; set; } = "Alex";
        public RequestType Type { get; set; } = RequestType.ExtraScreenTime;
        public string Title { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int RequestedMinutes { get; set; } = 30;
        public string TargetProcessName { get; set; } = string.Empty;
        public RequestStatus Status { get; set; } = RequestStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? RespondedAt { get; set; } = null;
    }

    public class GuardianSettings
    {
        public string DeviceId { get; set; } = string.Empty;
        public UserRole ActiveRole { get; set; } = UserRole.NotSelected;
        public string ActiveChildId { get; set; } = "default";
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public bool IsDefaultPassword { get; set; } = true;
        public int SessionTimeoutMinutes { get; set; } = 15;
        public bool StartWithWindows { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool WebProtectionEnabled { get; set; } = true;
        public bool BedtimeLockEnabled { get; set; } = true;
        public bool DarkTheme { get; set; } = true;
        public string LinkedWebUsername { get; set; } = string.Empty;
    }

    public enum CommandStatus
    {
        Pending,
        Delivered,
        Executing,
        Executed,
        Failed,
        Expired,
        DeviceOffline
    }

    public enum RemoteCommandType
    {
        LockDevice,
        UnlockDevice,
        BlockApp,
        BlockApplication,
        UnblockApp,
        UnblockApplication,
        BlockWebsite,
        UnblockWebsite,
        SetDailyLimit,
        GrantBonusTime,
        AddTime,
        RequestDecision,
        ActivateAfk,
        StartAfk,
        DeactivateAfk,
        EndAfk,
        RefreshPolicy
    }

    public class RemoteCommand
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ChildId { get; set; } = "default";
        public string DeviceId { get; set; } = Environment.MachineName;
        public RemoteCommandType CommandType { get; set; } = RemoteCommandType.LockDevice;
        public string PayloadJson { get; set; } = "{}";
        public CommandStatus Status { get; set; } = CommandStatus.Pending;
        public string CreatedBy { get; set; } = "Parent";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
        public DateTime? ExecutedAt { get; set; } = null;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class DeviceStatusRecord
    {
        public string DeviceId { get; set; } = Environment.MachineName;
        public string DeviceName { get; set; } = Environment.MachineName;
        public string ParentId { get; set; } = string.Empty;
        public string ChildId { get; set; } = "default";
        public bool IsOnline { get; set; } = true;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public bool IsGuardianRunning { get; set; } = true;
        public bool IsProtected { get; set; } = true;
        public long ScreenTimeUsedSeconds { get; set; } = 0;
        public int DailyLimitMinutes { get; set; } = 120;
        public bool IsAfkModeActive { get; set; } = false;
        public bool AfkActive { get => IsAfkModeActive; set => IsAfkModeActive = value; }
        public string ActiveProcessName { get; set; } = string.Empty;
        public string CurrentApp { get => ActiveProcessName; set => ActiveProcessName = value; }
        public string ActiveWindowTitle { get; set; } = string.Empty;
    }
}
