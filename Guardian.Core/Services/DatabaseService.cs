using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface IDatabaseService
    {
        void InitializeDatabase();
        GuardianSettings LoadSettings();
        void SaveSettings(GuardianSettings settings);

        // Child Profiles
        List<ChildProfile> GetAllChildren();
        ChildProfile? GetChild(string id);
        void SaveChild(ChildProfile child);
        void DeleteChild(string id);

        // Advanced Schedules
        List<AdvancedDaySchedule> GetAllAdvancedSchedules();
        AdvancedDaySchedule GetAdvancedSchedule(DayOfWeek day);
        void SaveAdvancedSchedule(AdvancedDaySchedule schedule);

        // Application Rules
        List<AppRule> GetAllAppRules();
        AppRule? GetAppRule(string processName);
        void SaveAppRule(AppRule rule);
        void DeleteAppRule(string processName);

        // Web Filter Rules
        List<WebFilterRule> GetWebRules();
        void SaveWebRule(WebFilterRule rule);
        void DeleteWebRule(string domain);

        // AFK Mode State & Log
        AfkState LoadAfkState();
        void SaveAfkState(AfkState state);
        void SaveAfkSession(AfkSessionRecord session);
        AfkSessionRecord? GetActiveAfkSession();
        List<AfkSessionRecord> GetAllAfkSessions();
        (int TodayCount, long TodaySeconds, int WeekCount, long WeekSeconds, int TotalCount, long TotalSeconds) GetAfkSummaryStats();

        // Daily Usage Records
        DailyUsageRecord GetOrCreateDailyUsage(DateTime date, string childId = "default");
        void SaveDailyUsage(DailyUsageRecord record);
        List<DailyUsageRecord> GetUsageForDateRange(DateTime start, DateTime end, string childId = "default");

        // Requests
        List<ChildRequest> GetRequests(RequestStatus? statusFilter = null);
        void SaveRequest(ChildRequest request);

        // Incidents
        void LogIncident(string eventType, string processName, string description);
        List<(DateTime Timestamp, string EventType, string ProcessName, string Description)> GetIncidents(int limit = 50);

        // Notifications
        List<GuardianNotification> GetNotifications(int limit = 30);
        int GetUnreadNotificationCount();
        void SaveNotification(GuardianNotification notif);
        void MarkAllNotificationsRead();

        // Remote Commands & Device Statuses
        void SaveRemoteCommand(RemoteCommand cmd);
        RemoteCommand? GetRemoteCommand(string id);
        List<RemoteCommand> GetPendingCommandsForDevice(string deviceId);
        void UpdateCommandStatus(string id, CommandStatus status, string? errorMessage = null);
        List<RemoteCommand> GetRecentCommands(int limit = 50);

        // User Accounts & Parent/Child Relationships
        UserAccount? GetUserByUsername(string username);
        UserAccount? GetUserById(string id);
        void SaveUserAccount(UserAccount user);
        List<ChildProfile> GetChildrenForParent(string parentId);
        List<DeviceStatusRecord> GetDevicesForParent(string parentId);
        List<DeviceStatusRecord> GetDevicesForChild(string childId);

        // Parent-Child Invitations & Active Relationships
        void SaveInvitation(ParentChildInvitation inv);
        ParentChildInvitation? GetInvitation(string id);
        List<ParentChildInvitation> GetInvitationsForParent(string parentUserId);
        List<ParentChildInvitation> GetPendingInvitationsForChild(string childUserId);
        void UpdateInvitationStatus(string id, InvitationStatus status, DateTime? respondedAt = null);

        void SaveRelationship(ParentChildRelationship rel);
        List<ParentChildRelationship> GetActiveRelationshipsForParent(string parentUserId);
        List<ParentChildRelationship> GetActiveRelationshipsForChild(string childUserId);
        bool IsParentConnectedToChild(string parentUserId, string childUserId);

        void SaveDeviceStatus(DeviceStatusRecord status);
        DeviceStatusRecord? GetDeviceStatus(string deviceId);
        List<DeviceStatusRecord> GetAllDeviceStatuses();
    }

    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;
        private static readonly object _dbLock = new object();

        public DatabaseService(string? customDbPath = null)
        {
            string dbPath = customDbPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Guardian",
                "guardian.db"
            );

            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _connectionString = $"Data Source={dbPath};";
            InitializeDatabase();
        }

        public void InitializeDatabase()
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;

                    -- 1. Settings
                    CREATE TABLE IF NOT EXISTS Settings (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);

                    -- 2. Users & UserAccounts
                    CREATE TABLE IF NOT EXISTS Users (
                        id TEXT PRIMARY KEY,
                        username TEXT NOT NULL,
                        role TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS UserAccounts (
                        id TEXT PRIMARY KEY,
                        username TEXT NOT NULL,
                        normalized_username TEXT NOT NULL UNIQUE,
                        password_hash TEXT NOT NULL,
                        password_salt TEXT NOT NULL,
                        role TEXT NOT NULL DEFAULT 'Parent',
                        parent_id TEXT NOT NULL DEFAULT '',
                        created_at TEXT NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1
                    );
                    CREATE TABLE IF NOT EXISTS user_accounts (
                        id TEXT PRIMARY KEY,
                        username TEXT NOT NULL,
                        normalized_username TEXT NOT NULL UNIQUE,
                        password_hash TEXT NOT NULL,
                        password_salt TEXT NOT NULL,
                        role TEXT NOT NULL DEFAULT 'Parent',
                        parent_id TEXT NOT NULL DEFAULT '',
                        created_at TEXT NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1
                    );

                    -- 3. ParentAuthentication
                    CREATE TABLE IF NOT EXISTS ParentAuthentication (
                        id TEXT PRIMARY KEY,
                        password_hash TEXT NOT NULL,
                        password_salt TEXT NOT NULL,
                        is_default INTEGER NOT NULL DEFAULT 1,
                        updated_at TEXT NOT NULL
                    );

                    -- 4. Children
                    CREATE TABLE IF NOT EXISTS Children (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        avatar TEXT NOT NULL,
                        age INTEGER NOT NULL DEFAULT 10,
                        is_active INTEGER NOT NULL DEFAULT 1,
                        daily_limit_minutes INTEGER NOT NULL DEFAULT 120,
                        bedtime TEXT NOT NULL,
                        bedtime_enabled INTEGER NOT NULL DEFAULT 1,
                        created_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS children (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        avatar TEXT NOT NULL,
                        age INTEGER NOT NULL DEFAULT 10,
                        is_active INTEGER NOT NULL DEFAULT 1,
                        daily_limit_minutes INTEGER NOT NULL DEFAULT 120,
                        bedtime TEXT NOT NULL,
                        bedtime_enabled INTEGER NOT NULL DEFAULT 1,
                        created_at TEXT NOT NULL
                    );

                    -- 5. Schedules & ScheduleDays
                    CREATE TABLE IF NOT EXISTS Schedules (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1
                    );
                    CREATE TABLE IF NOT EXISTS ScheduleDays (
                        day_of_week INTEGER PRIMARY KEY,
                        limit_minutes INTEGER NOT NULL,
                        has_allowed_hours INTEGER NOT NULL DEFAULT 0,
                        start_time TEXT NOT NULL,
                        end_time TEXT NOT NULL,
                        bedtime_enabled INTEGER NOT NULL DEFAULT 1,
                        bedtime TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS advanced_schedules (
                        day_of_week INTEGER PRIMARY KEY,
                        limit_minutes INTEGER NOT NULL,
                        has_allowed_hours INTEGER NOT NULL DEFAULT 0,
                        start_time TEXT NOT NULL,
                        end_time TEXT NOT NULL,
                        bedtime_enabled INTEGER NOT NULL DEFAULT 1,
                        bedtime TEXT NOT NULL
                    );

                    -- 6. AppRules
                    CREATE TABLE IF NOT EXISTS AppRules (
                        process_name TEXT PRIMARY KEY,
                        display_name TEXT NOT NULL,
                        is_blocked INTEGER NOT NULL DEFAULT 0,
                        daily_limit_minutes INTEGER NOT NULL DEFAULT 0,
                        category TEXT NOT NULL DEFAULT 'General'
                    );
                    CREATE TABLE IF NOT EXISTS app_rules (
                        process_name TEXT PRIMARY KEY,
                        display_name TEXT NOT NULL,
                        is_blocked INTEGER NOT NULL DEFAULT 0,
                        daily_limit_minutes INTEGER NOT NULL DEFAULT 0,
                        category TEXT NOT NULL DEFAULT 'General'
                    );

                    -- 7. WebsiteRules
                    CREATE TABLE IF NOT EXISTS WebsiteRules (
                        domain TEXT PRIMARY KEY,
                        category TEXT NOT NULL,
                        is_blocked INTEGER NOT NULL DEFAULT 1,
                        reason TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS web_rules (
                        domain TEXT PRIMARY KEY,
                        category TEXT NOT NULL,
                        is_blocked INTEGER NOT NULL DEFAULT 1,
                        reason TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );

                    -- 8. DailyUsage
                    CREATE TABLE IF NOT EXISTS DailyUsage (
                        date TEXT NOT NULL,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        total_seconds INTEGER NOT NULL DEFAULT 0,
                        bonus_seconds INTEGER NOT NULL DEFAULT 0,
                        is_locked INTEGER NOT NULL DEFAULT 0,
                        lock_reason TEXT NOT NULL DEFAULT '',
                        lock_date TEXT NOT NULL DEFAULT '',
                        parent_unlocked_today INTEGER NOT NULL DEFAULT 0,
                        PRIMARY KEY (date, child_id)
                    );
                    CREATE TABLE IF NOT EXISTS daily_usage (
                        date TEXT NOT NULL,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        total_seconds INTEGER NOT NULL DEFAULT 0,
                        bonus_seconds INTEGER NOT NULL DEFAULT 0,
                        is_locked INTEGER NOT NULL DEFAULT 0,
                        lock_reason TEXT NOT NULL DEFAULT '',
                        lock_date TEXT NOT NULL DEFAULT '',
                        parent_unlocked_today INTEGER NOT NULL DEFAULT 0,
                        PRIMARY KEY (date, child_id)
                    );

                    -- 9. AppUsage
                    CREATE TABLE IF NOT EXISTS AppUsage (
                        date TEXT NOT NULL,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        process_name TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        seconds_used INTEGER NOT NULL DEFAULT 0,
                        last_active TEXT NOT NULL,
                        PRIMARY KEY (date, child_id, process_name)
                    );
                    CREATE TABLE IF NOT EXISTS app_usage (
                        date TEXT NOT NULL,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        process_name TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        seconds_used INTEGER NOT NULL DEFAULT 0,
                        last_active TEXT NOT NULL,
                        PRIMARY KEY (date, child_id, process_name)
                    );

                    -- 10. TimeRequests
                    CREATE TABLE IF NOT EXISTS TimeRequests (
                        id TEXT PRIMARY KEY,
                        child_name TEXT NOT NULL DEFAULT 'Alex',
                        type TEXT NOT NULL,
                        title TEXT NOT NULL,
                        reason TEXT NOT NULL,
                        requested_minutes INTEGER NOT NULL,
                        target_process TEXT NOT NULL,
                        status TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        responded_at TEXT
                    );
                    CREATE TABLE IF NOT EXISTS requests (
                        id TEXT PRIMARY KEY,
                        child_name TEXT NOT NULL DEFAULT 'Alex',
                        type TEXT NOT NULL,
                        title TEXT NOT NULL,
                        reason TEXT NOT NULL,
                        requested_minutes INTEGER NOT NULL,
                        target_process TEXT NOT NULL,
                        status TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        responded_at TEXT
                    );

                    -- 11. ActivityLog & Incidents
                    CREATE TABLE IF NOT EXISTS ActivityLog (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp TEXT NOT NULL,
                        event_type TEXT NOT NULL,
                        process_name TEXT NOT NULL,
                        description TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS incidents (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp TEXT NOT NULL,
                        event_type TEXT NOT NULL,
                        process_name TEXT NOT NULL,
                        description TEXT NOT NULL
                    );

                    -- 12. Notifications
                    CREATE TABLE IF NOT EXISTS Notifications (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        message TEXT NOT NULL,
                        severity TEXT NOT NULL,
                        is_read INTEGER NOT NULL DEFAULT 0,
                        timestamp TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS notifications (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        message TEXT NOT NULL,
                        severity TEXT NOT NULL,
                        is_read INTEGER NOT NULL DEFAULT 0,
                        timestamp TEXT NOT NULL
                    );

                    -- 13. DeviceState
                    CREATE TABLE IF NOT EXISTS DeviceState (
                        key TEXT PRIMARY KEY,
                        is_locked INTEGER NOT NULL DEFAULT 0,
                        lock_reason TEXT NOT NULL DEFAULT '',
                        lock_date TEXT NOT NULL DEFAULT '',
                        parent_unlocked_today INTEGER NOT NULL DEFAULT 0,
                        updated_at TEXT NOT NULL
                    );

                    -- 14. ProtectionState
                    CREATE TABLE IF NOT EXISTS ProtectionState (
                        service_name TEXT PRIMARY KEY,
                        is_running INTEGER NOT NULL DEFAULT 1,
                        status_summary TEXT NOT NULL,
                        last_check TEXT NOT NULL
                    );

                    -- 15. AuditLog
                    CREATE TABLE IF NOT EXISTS AuditLog (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp TEXT NOT NULL,
                        action TEXT NOT NULL,
                        performed_by TEXT NOT NULL,
                        details TEXT NOT NULL
                    );

                    -- 16. AfkState
                    CREATE TABLE IF NOT EXISTS afk_state (
                        key TEXT PRIMARY KEY,
                        is_active INTEGER NOT NULL DEFAULT 0,
                        started_at TEXT,
                        ended_at TEXT,
                        duration_minutes INTEGER NOT NULL DEFAULT 0,
                        activated_by TEXT NOT NULL DEFAULT 'Parent'
                    );

                    -- 17. AfkSessions
                    CREATE TABLE IF NOT EXISTS AfkSessions (
                        id TEXT PRIMARY KEY,
                        start_timestamp TEXT NOT NULL,
                        end_timestamp TEXT,
                        duration_seconds INTEGER NOT NULL DEFAULT 0,
                        activation_method TEXT NOT NULL DEFAULT 'Parent Password',
                        activated_by TEXT NOT NULL DEFAULT 'Parent',
                        ended_by TEXT NOT NULL DEFAULT '',
                        end_reason TEXT NOT NULL DEFAULT 'Active',
                        created_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS afk_sessions (
                        id TEXT PRIMARY KEY,
                        start_timestamp TEXT NOT NULL,
                        end_timestamp TEXT,
                        duration_seconds INTEGER NOT NULL DEFAULT 0,
                        activation_method TEXT NOT NULL DEFAULT 'Parent Password',
                        activated_by TEXT NOT NULL DEFAULT 'Parent',
                        ended_by TEXT NOT NULL DEFAULT '',
                        end_reason TEXT NOT NULL DEFAULT 'Active',
                        created_at TEXT NOT NULL
                    );

                    -- 18. RemoteCommands
                    CREATE TABLE IF NOT EXISTS RemoteCommands (
                        id TEXT PRIMARY KEY,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        device_id TEXT NOT NULL,
                        command_type TEXT NOT NULL,
                        payload_json TEXT NOT NULL DEFAULT '{}',
                        status TEXT NOT NULL DEFAULT 'Pending',
                        created_by TEXT NOT NULL DEFAULT 'Parent',
                        created_at TEXT NOT NULL,
                        executed_at TEXT,
                        error_message TEXT NOT NULL DEFAULT ''
                    );

                    -- 19. DeviceStatuses
                    CREATE TABLE IF NOT EXISTS DeviceStatuses (
                        device_id TEXT PRIMARY KEY,
                        device_name TEXT NOT NULL,
                        child_id TEXT NOT NULL DEFAULT 'default',
                        is_online INTEGER NOT NULL DEFAULT 1,
                        last_seen TEXT NOT NULL,
                        is_guardian_running INTEGER NOT NULL DEFAULT 1,
                        is_protected INTEGER NOT NULL DEFAULT 1,
                        screen_time_used_seconds INTEGER NOT NULL DEFAULT 0,
                        current_app TEXT NOT NULL DEFAULT 'Idle',
                        afk_active INTEGER NOT NULL DEFAULT 0
                    );

                    -- 20. ParentChildInvitations & Relationships
                    CREATE TABLE IF NOT EXISTS ParentChildInvitations (
                        id TEXT PRIMARY KEY,
                        parent_user_id TEXT NOT NULL,
                        parent_username TEXT NOT NULL,
                        child_user_id TEXT NOT NULL,
                        child_username TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Pending',
                        created_at TEXT NOT NULL,
                        responded_at TEXT,
                        expires_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS ParentChildRelationships (
                        id TEXT PRIMARY KEY,
                        parent_user_id TEXT NOT NULL,
                        child_user_id TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1
                    );
                ";
                cmd.ExecuteNonQuery();

                // Safe migration for pre-existing SQLite databases
                string[] columnMigrations = new[]
                {
                    "ALTER TABLE daily_usage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                    "ALTER TABLE daily_usage ADD COLUMN lock_reason TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE daily_usage ADD COLUMN lock_date TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE daily_usage ADD COLUMN parent_unlocked_today INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE daily_usage ADD COLUMN afk_seconds INTEGER NOT NULL DEFAULT 0;",

                    "ALTER TABLE DailyUsage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                    "ALTER TABLE DailyUsage ADD COLUMN lock_reason TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE DailyUsage ADD COLUMN lock_date TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE DailyUsage ADD COLUMN parent_unlocked_today INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE DailyUsage ADD COLUMN afk_seconds INTEGER NOT NULL DEFAULT 0;",

                    "ALTER TABLE app_usage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                    "ALTER TABLE AppUsage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",

                    "ALTER TABLE requests ADD COLUMN child_name TEXT NOT NULL DEFAULT 'Alex';",
                    "ALTER TABLE TimeRequests ADD COLUMN child_name TEXT NOT NULL DEFAULT 'Alex';",

                    "ALTER TABLE children ADD COLUMN parent_id TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE children ADD COLUMN user_id TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE Children ADD COLUMN parent_id TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE Children ADD COLUMN user_id TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE DeviceStatuses ADD COLUMN parent_id TEXT NOT NULL DEFAULT '';",
                    "ALTER TABLE RemoteCommands ADD COLUMN expires_at TEXT NOT NULL DEFAULT '';"
                };

                foreach (var sql in columnMigrations)
                {
                    try
                    {
                        using var mCmd = conn.CreateCommand();
                        mCmd.CommandText = sql;
                        mCmd.ExecuteNonQuery();
                    }
                    catch { /* Column already exists */ }
                }
            }
        }

        public GuardianSettings LoadSettings()
        {
            lock (_dbLock)
            {
                var s = new GuardianSettings();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM settings;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string val = reader.GetString(1);

                    switch (key)
                    {
                        case "DeviceId": s.DeviceId = val; break;
                        case "ActiveRole": if (Enum.TryParse<UserRole>(val, out var role)) s.ActiveRole = role; break;
                        case "ActiveChildId": s.ActiveChildId = val; break;
                        case "PasswordHash": s.PasswordHash = val; break;
                        case "PasswordSalt": s.PasswordSalt = val; break;
                        case "IsDefaultPassword": s.IsDefaultPassword = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "SessionTimeoutMinutes": if (int.TryParse(val, out int t)) s.SessionTimeoutMinutes = t; break;
                        case "StartWithWindows": s.StartWithWindows = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "MinimizeToTrayOnClose": s.MinimizeToTrayOnClose = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "WebProtectionEnabled": s.WebProtectionEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "BedtimeLockEnabled": s.BedtimeLockEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "DarkTheme": s.DarkTheme = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "LinkedWebUsername": s.LinkedWebUsername = val; break;
                    }
                }

                return s;
            }
        }

        public void SaveSettings(GuardianSettings s)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();

                var dict = new Dictionary<string, string>
                {
                    { "DeviceId", s.DeviceId ?? "" },
                    { "ActiveRole", s.ActiveRole.ToString() },
                    { "ActiveChildId", s.ActiveChildId },
                    { "PasswordHash", s.PasswordHash },
                    { "PasswordSalt", s.PasswordSalt },
                    { "IsDefaultPassword", s.IsDefaultPassword ? "1" : "0" },
                    { "SessionTimeoutMinutes", s.SessionTimeoutMinutes.ToString() },
                    { "StartWithWindows", s.StartWithWindows ? "1" : "0" },
                    { "MinimizeToTrayOnClose", s.MinimizeToTrayOnClose ? "1" : "0" },
                    { "WebProtectionEnabled", s.WebProtectionEnabled ? "1" : "0" },
                    { "BedtimeLockEnabled", s.BedtimeLockEnabled ? "1" : "0" },
                    { "DarkTheme", s.DarkTheme ? "1" : "0" },
                    { "LinkedWebUsername", s.LinkedWebUsername ?? "" }
                };

                foreach (var kvp in dict)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = "INSERT INTO settings (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = @v;";
                    cmd.Parameters.AddWithValue("@k", kvp.Key);
                    cmd.Parameters.AddWithValue("@v", kvp.Value);
                    cmd.ExecuteNonQuery();
                }

                trans.Commit();
            }
        }

        // Child Profiles
        public List<ChildProfile> GetAllChildren()
        {
            lock (_dbLock)
            {
                var list = new List<ChildProfile>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, name, avatar, age, is_active, daily_limit_minutes, bedtime, bedtime_enabled, created_at, parent_id, user_id FROM children ORDER BY name;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new ChildProfile
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Avatar = reader.GetString(2),
                        Age = reader.GetInt32(3),
                        IsActive = reader.GetInt32(4) == 1,
                        DailyLimitMinutes = reader.GetInt32(5),
                        Bedtime = TimeSpan.TryParse(reader.GetString(6), out var bt) ? bt : new TimeSpan(21, 30, 0),
                        BedtimeEnabled = reader.GetInt32(7) == 1,
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca : DateTime.Now,
                        ParentId = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        UserId = reader.IsDBNull(10) ? "" : reader.GetString(10)
                    });
                }
                return list;
            }
        }

        public ChildProfile? GetChild(string id)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, name, avatar, age, is_active, daily_limit_minutes, bedtime, bedtime_enabled, created_at, parent_id, user_id FROM children WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new ChildProfile
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Avatar = reader.GetString(2),
                        Age = reader.GetInt32(3),
                        IsActive = reader.GetInt32(4) == 1,
                        DailyLimitMinutes = reader.GetInt32(5),
                        Bedtime = TimeSpan.TryParse(reader.GetString(6), out var bt) ? bt : new TimeSpan(21, 30, 0),
                        BedtimeEnabled = reader.GetInt32(7) == 1,
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca : DateTime.Now,
                        ParentId = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        UserId = reader.IsDBNull(10) ? "" : reader.GetString(10)
                    };
                }
                return null;
            }
        }

        public void SaveChild(ChildProfile child)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO children (id, name, avatar, age, is_active, daily_limit_minutes, bedtime, bedtime_enabled, created_at, parent_id, user_id)
                    VALUES (@id, @n, @av, @ag, @act, @dlm, @bt, @bte, @ca, @pid, @uid)
                    ON CONFLICT(id) DO UPDATE SET
                        name = @n,
                        avatar = @av,
                        age = @ag,
                        is_active = @act,
                        daily_limit_minutes = @dlm,
                        bedtime = @bt,
                        bedtime_enabled = @bte,
                        parent_id = @pid,
                        user_id = @uid;
                ";
                cmd.Parameters.AddWithValue("@id", child.Id);
                cmd.Parameters.AddWithValue("@n", child.Name);
                cmd.Parameters.AddWithValue("@av", child.Avatar);
                cmd.Parameters.AddWithValue("@ag", child.Age);
                cmd.Parameters.AddWithValue("@act", child.IsActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@dlm", child.DailyLimitMinutes);
                cmd.Parameters.AddWithValue("@bt", child.Bedtime.ToString(@"hh\:mm\:ss"));
                cmd.Parameters.AddWithValue("@bte", child.BedtimeEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@ca", child.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@pid", child.ParentId ?? "");
                cmd.Parameters.AddWithValue("@uid", child.UserId ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteChild(string id)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM children WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        // Advanced Schedules
        public List<AdvancedDaySchedule> GetAllAdvancedSchedules()
        {
            lock (_dbLock)
            {
                var list = new List<AdvancedDaySchedule>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT day_of_week, limit_minutes, has_allowed_hours, start_time, end_time, bedtime_enabled, bedtime FROM advanced_schedules ORDER BY day_of_week;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new AdvancedDaySchedule
                    {
                        Day = (DayOfWeek)reader.GetInt32(0),
                        LimitMinutes = reader.GetInt32(1),
                        HasAllowedHours = reader.GetInt32(2) == 1,
                        AllowedStartTime = TimeSpan.TryParse(reader.GetString(3), out var st) ? st : new TimeSpan(17, 0, 0),
                        AllowedEndTime = TimeSpan.TryParse(reader.GetString(4), out var et) ? et : new TimeSpan(20, 0, 0),
                        BedtimeEnabled = reader.GetInt32(5) == 1,
                        Bedtime = TimeSpan.TryParse(reader.GetString(6), out var bt) ? bt : new TimeSpan(21, 30, 0)
                    });
                }
                return list;
            }
        }

        public AdvancedDaySchedule GetAdvancedSchedule(DayOfWeek day)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT day_of_week, limit_minutes, has_allowed_hours, start_time, end_time, bedtime_enabled, bedtime FROM advanced_schedules WHERE day_of_week = @d;";
                cmd.Parameters.AddWithValue("@d", (int)day);
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new AdvancedDaySchedule
                    {
                        Day = day,
                        LimitMinutes = reader.GetInt32(1),
                        HasAllowedHours = reader.GetInt32(2) == 1,
                        AllowedStartTime = TimeSpan.TryParse(reader.GetString(3), out var st) ? st : new TimeSpan(17, 0, 0),
                        AllowedEndTime = TimeSpan.TryParse(reader.GetString(4), out var et) ? et : new TimeSpan(20, 0, 0),
                        BedtimeEnabled = reader.GetInt32(5) == 1,
                        Bedtime = TimeSpan.TryParse(reader.GetString(6), out var bt) ? bt : new TimeSpan(21, 30, 0)
                    };
                }

                return new AdvancedDaySchedule { Day = day, LimitMinutes = 120 };
            }
        }

        public void SaveAdvancedSchedule(AdvancedDaySchedule s)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO advanced_schedules (day_of_week, limit_minutes, has_allowed_hours, start_time, end_time, bedtime_enabled, bedtime)
                    VALUES (@d, @lm, @hah, @st, @et, @bte, @bt)
                    ON CONFLICT(day_of_week) DO UPDATE SET
                        limit_minutes = @lm,
                        has_allowed_hours = @hah,
                        start_time = @st,
                        end_time = @et,
                        bedtime_enabled = @bte,
                        bedtime = @bt;
                ";
                cmd.Parameters.AddWithValue("@d", (int)s.Day);
                cmd.Parameters.AddWithValue("@lm", s.LimitMinutes);
                cmd.Parameters.AddWithValue("@hah", s.HasAllowedHours ? 1 : 0);
                cmd.Parameters.AddWithValue("@st", s.AllowedStartTime.ToString(@"hh\:mm\:ss"));
                cmd.Parameters.AddWithValue("@et", s.AllowedEndTime.ToString(@"hh\:mm\:ss"));
                cmd.Parameters.AddWithValue("@bte", s.BedtimeEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@bt", s.Bedtime.ToString(@"hh\:mm\:ss"));
                cmd.ExecuteNonQuery();
            }
        }

        // App Rules
        public List<AppRule> GetAllAppRules()
        {
            lock (_dbLock)
            {
                var list = new List<AppRule>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT process_name, display_name, is_blocked, daily_limit_minutes, category FROM app_rules ORDER BY display_name;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new AppRule
                    {
                        ProcessName = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        IsBlocked = reader.GetInt32(2) == 1,
                        DailyLimitMinutes = reader.GetInt32(3),
                        Category = reader.GetString(4)
                    });
                }
                return list;
            }
        }

        public AppRule? GetAppRule(string processName)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT process_name, display_name, is_blocked, daily_limit_minutes, category FROM app_rules WHERE process_name = @p;";
                cmd.Parameters.AddWithValue("@p", processName.ToLowerInvariant());
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new AppRule
                    {
                        ProcessName = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        IsBlocked = reader.GetInt32(2) == 1,
                        DailyLimitMinutes = reader.GetInt32(3),
                        Category = reader.GetString(4)
                    };
                }
                return null;
            }
        }

        public void SaveAppRule(AppRule rule)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO app_rules (process_name, display_name, is_blocked, daily_limit_minutes, category)
                    VALUES (@p, @d, @b, @l, @c)
                    ON CONFLICT(process_name) DO UPDATE SET
                        display_name = @d,
                        is_blocked = @b,
                        daily_limit_minutes = @l,
                        category = @c;
                ";
                cmd.Parameters.AddWithValue("@p", rule.ProcessName.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@d", rule.DisplayName);
                cmd.Parameters.AddWithValue("@b", rule.IsBlocked ? 1 : 0);
                cmd.Parameters.AddWithValue("@l", rule.DailyLimitMinutes);
                cmd.Parameters.AddWithValue("@c", rule.Category);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteAppRule(string processName)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM app_rules WHERE process_name = @p;";
                cmd.Parameters.AddWithValue("@p", processName.ToLowerInvariant());
                cmd.ExecuteNonQuery();
            }
        }

        // Web Rules
        public List<WebFilterRule> GetWebRules()
        {
            lock (_dbLock)
            {
                var list = new List<WebFilterRule>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT domain, category, is_blocked, reason, created_at FROM web_rules ORDER BY domain;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new WebFilterRule
                    {
                        Domain = reader.GetString(0),
                        Category = reader.GetString(1),
                        IsBlocked = reader.GetInt32(2) == 1,
                        Reason = reader.GetString(3),
                        CreatedAt = DateTime.TryParse(reader.GetString(4), out var ca) ? ca : DateTime.Now
                    });
                }
                return list;
            }
        }

        public void SaveWebRule(WebFilterRule rule)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO web_rules (domain, category, is_blocked, reason, created_at)
                    VALUES (@d, @c, @b, @r, @ca)
                    ON CONFLICT(domain) DO UPDATE SET
                        category = @c,
                        is_blocked = @b,
                        reason = @r;
                ";
                cmd.Parameters.AddWithValue("@d", rule.Domain.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@c", rule.Category);
                cmd.Parameters.AddWithValue("@b", rule.IsBlocked ? 1 : 0);
                cmd.Parameters.AddWithValue("@r", rule.Reason);
                cmd.Parameters.AddWithValue("@ca", rule.CreatedAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteWebRule(string domain)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM web_rules WHERE domain = @d;";
                cmd.Parameters.AddWithValue("@d", domain.ToLowerInvariant());
                cmd.ExecuteNonQuery();
            }
        }

        // AFK Mode State
        public AfkState LoadAfkState()
        {
            lock (_dbLock)
            {
                var state = new AfkState();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT is_active, started_at, ended_at, duration_minutes, activated_by FROM afk_state WHERE key = 'current';";
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    state.IsAfkModeActive = reader.GetInt32(0) == 1;
                    state.AfkStartedAt = reader.IsDBNull(1) ? null : (DateTime.TryParse(reader.GetString(1), out var st) ? st : null);
                    state.AfkEndedAt = reader.IsDBNull(2) ? null : (DateTime.TryParse(reader.GetString(2), out var et) ? et : null);
                    state.AfkDurationMinutes = reader.GetInt32(3);
                    state.ActivatedBy = reader.IsDBNull(4) ? "Parent" : reader.GetString(4);
                }

                return state;
            }
        }

        public void SaveAfkState(AfkState state)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO afk_state (key, is_active, started_at, ended_at, duration_minutes, activated_by)
                    VALUES ('current', @act, @sa, @ea, @dm, @ab)
                    ON CONFLICT(key) DO UPDATE SET
                        is_active = @act,
                        started_at = @sa,
                        ended_at = @ea,
                        duration_minutes = @dm,
                        activated_by = @ab;
                ";
                cmd.Parameters.AddWithValue("@act", state.IsAfkModeActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@sa", state.AfkStartedAt.HasValue ? state.AfkStartedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ea", state.AfkEndedAt.HasValue ? state.AfkEndedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@dm", state.AfkDurationMinutes);
                cmd.Parameters.AddWithValue("@ab", state.ActivatedBy ?? "Parent");
                cmd.ExecuteNonQuery();
            }
        }

        // AFK Sessions Log
        public void SaveAfkSession(AfkSessionRecord session)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO afk_sessions (id, start_timestamp, end_timestamp, duration_seconds, activation_method, activated_by, ended_by, end_reason, created_at)
                    VALUES (@id, @st, @et, @dur, @am, @ab, @eb, @er, @ca)
                    ON CONFLICT(id) DO UPDATE SET
                        start_timestamp = @st,
                        end_timestamp = @et,
                        duration_seconds = @dur,
                        activation_method = @am,
                        activated_by = @ab,
                        ended_by = @eb,
                        end_reason = @er;
                ";
                cmd.Parameters.AddWithValue("@id", session.Id);
                cmd.Parameters.AddWithValue("@st", session.StartTimestamp.ToString("o"));
                cmd.Parameters.AddWithValue("@et", session.EndTimestamp.HasValue ? session.EndTimestamp.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@dur", session.DurationSeconds);
                cmd.Parameters.AddWithValue("@am", session.ActivationMethod ?? "Parent Password");
                cmd.Parameters.AddWithValue("@ab", session.ActivatedBy ?? "Parent");
                cmd.Parameters.AddWithValue("@eb", session.EndedBy ?? "");
                cmd.Parameters.AddWithValue("@er", session.EndReason ?? "Active");
                cmd.Parameters.AddWithValue("@ca", session.CreatedAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public AfkSessionRecord? GetActiveAfkSession()
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, start_timestamp, end_timestamp, duration_seconds, activation_method, activated_by, ended_by, end_reason, created_at FROM afk_sessions WHERE end_timestamp IS NULL OR end_reason = 'Active' ORDER BY start_timestamp DESC LIMIT 1;";
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new AfkSessionRecord
                    {
                        Id = reader.GetString(0),
                        StartTimestamp = DateTime.TryParse(reader.GetString(1), out var st) ? (st.Kind == DateTimeKind.Utc ? st : DateTime.SpecifyKind(st, DateTimeKind.Utc)) : DateTime.UtcNow,
                        EndTimestamp = reader.IsDBNull(2) ? null : (DateTime.TryParse(reader.GetString(2), out var et) ? (et.Kind == DateTimeKind.Utc ? et : DateTime.SpecifyKind(et, DateTimeKind.Utc)) : null),
                        DurationSeconds = reader.GetInt64(3),
                        ActivationMethod = reader.GetString(4),
                        ActivatedBy = reader.GetString(5),
                        EndedBy = reader.GetString(6),
                        EndReason = reader.GetString(7),
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca.ToUniversalTime() : DateTime.UtcNow
                    };
                }
                return null;
            }
        }

        public List<AfkSessionRecord> GetAllAfkSessions()
        {
            lock (_dbLock)
            {
                var list = new List<AfkSessionRecord>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, start_timestamp, end_timestamp, duration_seconds, activation_method, activated_by, ended_by, end_reason, created_at FROM afk_sessions ORDER BY start_timestamp DESC;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new AfkSessionRecord
                    {
                        Id = reader.GetString(0),
                        StartTimestamp = DateTime.TryParse(reader.GetString(1), out var st) ? (st.Kind == DateTimeKind.Utc ? st : DateTime.SpecifyKind(st, DateTimeKind.Utc)) : DateTime.UtcNow,
                        EndTimestamp = reader.IsDBNull(2) ? null : (DateTime.TryParse(reader.GetString(2), out var et) ? (et.Kind == DateTimeKind.Utc ? et : DateTime.SpecifyKind(et, DateTimeKind.Utc)) : null),
                        DurationSeconds = reader.GetInt64(3),
                        ActivationMethod = reader.GetString(4),
                        ActivatedBy = reader.GetString(5),
                        EndedBy = reader.GetString(6),
                        EndReason = reader.GetString(7),
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca.ToUniversalTime() : DateTime.UtcNow
                    });
                }
                return list;
            }
        }

        public (int TodayCount, long TodaySeconds, int WeekCount, long WeekSeconds, int TotalCount, long TotalSeconds) GetAfkSummaryStats()
        {
            var sessions = GetAllAfkSessions();
            DateTime todayDate = DateTime.Today;
            DateTime weekDate = DateTime.Today.AddDays(-6);

            int todayCount = 0;
            long todaySeconds = 0;
            int weekCount = 0;
            long weekSeconds = 0;
            int totalCount = sessions.Count;
            long totalSeconds = 0;

            foreach (var s in sessions)
            {
                long dur = (long)s.CalculatedDuration.TotalSeconds;
                if (dur < 0) dur = 0;
                totalSeconds += dur;

                if (s.LocalStartTime.Date == todayDate)
                {
                    todayCount++;
                    todaySeconds += dur;
                }

                if (s.LocalStartTime.Date >= weekDate)
                {
                    weekCount++;
                    weekSeconds += dur;
                }
            }

            return (todayCount, todaySeconds, weekCount, weekSeconds, totalCount, totalSeconds);
        }

        // Daily Usage
        public DailyUsageRecord GetOrCreateDailyUsage(DateTime date, string childId = "default")
        {
            lock (_dbLock)
            {
                string dateStr = date.ToString("yyyy-MM-dd");
                var record = new DailyUsageRecord { DateString = dateStr, ChildId = childId };

                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                try
                {
                    ReadDailyUsageRecord(conn, dateStr, childId, record);
                }
                catch (SqliteException)
                {
                    // Run migration to add missing columns and retry once
                    MigrateDatabaseSchema(conn);
                    ReadDailyUsageRecord(conn, dateStr, childId, record);
                }

                return record;
            }
        }

        private void ReadDailyUsageRecord(SqliteConnection conn, string dateStr, string childId, DailyUsageRecord record)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT total_seconds, bonus_seconds, is_locked, lock_reason, afk_seconds FROM daily_usage WHERE date = @d AND child_id = @c;";
                cmd.Parameters.AddWithValue("@d", dateStr);
                cmd.Parameters.AddWithValue("@c", childId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    record.TotalDeviceSecondsUsed = reader.GetInt64(0);
                    record.ManualBonusSeconds = reader.GetInt64(1);
                    record.IsDeviceLocked = reader.GetInt32(2) == 1;
                    record.LockReason = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    record.TotalAfkSecondsUsed = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetInt64(4) : 0;
                }
            }

            // Load App Usages
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT process_name, display_name, seconds_used, last_active FROM app_usage WHERE date = @d AND child_id = @c;";
                cmd.Parameters.AddWithValue("@d", dateStr);
                cmd.Parameters.AddWithValue("@c", childId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string pName = reader.GetString(0);
                    record.AppUsages[pName] = new AppUsageEntry
                    {
                        ProcessName = pName,
                        DisplayName = reader.GetString(1),
                        SecondsUsed = reader.GetInt64(2),
                        LastActive = DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.Now
                    };
                }
            }
        }

        private void MigrateDatabaseSchema(SqliteConnection conn)
        {
            string[] columnMigrations = new[]
            {
                "ALTER TABLE daily_usage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                "ALTER TABLE daily_usage ADD COLUMN lock_reason TEXT NOT NULL DEFAULT '';",
                "ALTER TABLE daily_usage ADD COLUMN lock_date TEXT NOT NULL DEFAULT '';",
                "ALTER TABLE daily_usage ADD COLUMN parent_unlocked_today INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE daily_usage ADD COLUMN afk_seconds INTEGER NOT NULL DEFAULT 0;",

                "ALTER TABLE DailyUsage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                "ALTER TABLE DailyUsage ADD COLUMN lock_reason TEXT NOT NULL DEFAULT '';",
                "ALTER TABLE DailyUsage ADD COLUMN lock_date TEXT NOT NULL DEFAULT '';",
                "ALTER TABLE DailyUsage ADD COLUMN parent_unlocked_today INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE DailyUsage ADD COLUMN afk_seconds INTEGER NOT NULL DEFAULT 0;",

                "ALTER TABLE app_usage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",
                "ALTER TABLE AppUsage ADD COLUMN child_id TEXT NOT NULL DEFAULT 'default';",

                "ALTER TABLE requests ADD COLUMN child_name TEXT NOT NULL DEFAULT 'Alex';",
                "ALTER TABLE TimeRequests ADD COLUMN child_name TEXT NOT NULL DEFAULT 'Alex';"
            };

            foreach (var sql in columnMigrations)
            {
                try
                {
                    using var mCmd = conn.CreateCommand();
                    mCmd.CommandText = sql;
                    mCmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public void SaveDailyUsage(DailyUsageRecord record)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();

                // 1. Update daily_usage, insert if no row updated
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                        UPDATE daily_usage SET
                            total_seconds = @ts,
                            bonus_seconds = @bs,
                            is_locked = @il,
                            lock_reason = @lr,
                            afk_seconds = @as
                        WHERE date = @d AND child_id = @c;
                    ";
                    cmd.Parameters.AddWithValue("@d", record.DateString);
                    cmd.Parameters.AddWithValue("@c", record.ChildId);
                    cmd.Parameters.AddWithValue("@ts", record.TotalDeviceSecondsUsed);
                    cmd.Parameters.AddWithValue("@bs", record.ManualBonusSeconds);
                    cmd.Parameters.AddWithValue("@il", record.IsDeviceLocked ? 1 : 0);
                    cmd.Parameters.AddWithValue("@lr", record.LockReason ?? "");
                    cmd.Parameters.AddWithValue("@as", record.TotalAfkSecondsUsed);

                    int rows = cmd.ExecuteNonQuery();
                    if (rows == 0)
                    {
                        using var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = trans;
                        insertCmd.CommandText = @"
                            INSERT INTO daily_usage (date, child_id, total_seconds, bonus_seconds, is_locked, lock_reason, afk_seconds)
                            VALUES (@d, @c, @ts, @bs, @il, @lr, @as);
                        ";
                        insertCmd.Parameters.AddWithValue("@d", record.DateString);
                        insertCmd.Parameters.AddWithValue("@c", record.ChildId);
                        insertCmd.Parameters.AddWithValue("@ts", record.TotalDeviceSecondsUsed);
                        insertCmd.Parameters.AddWithValue("@bs", record.ManualBonusSeconds);
                        insertCmd.Parameters.AddWithValue("@il", record.IsDeviceLocked ? 1 : 0);
                        insertCmd.Parameters.AddWithValue("@lr", record.LockReason ?? "");
                        insertCmd.Parameters.AddWithValue("@as", record.TotalAfkSecondsUsed);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                // 2. Update app_usage, insert if no row updated
                foreach (var kvp in record.AppUsages)
                {
                    var app = kvp.Value;
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                        UPDATE app_usage SET
                            display_name = @dn,
                            seconds_used = @su,
                            last_active = @la
                        WHERE date = @d AND child_id = @c AND process_name = @pn;
                    ";
                    cmd.Parameters.AddWithValue("@d", record.DateString);
                    cmd.Parameters.AddWithValue("@c", record.ChildId);
                    cmd.Parameters.AddWithValue("@pn", app.ProcessName.ToLowerInvariant());
                    cmd.Parameters.AddWithValue("@dn", app.DisplayName);
                    cmd.Parameters.AddWithValue("@su", app.SecondsUsed);
                    cmd.Parameters.AddWithValue("@la", app.LastActive.ToString("o"));

                    int rows = cmd.ExecuteNonQuery();
                    if (rows == 0)
                    {
                        using var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = trans;
                        insertCmd.CommandText = @"
                            INSERT INTO app_usage (date, child_id, process_name, display_name, seconds_used, last_active)
                            VALUES (@d, @c, @pn, @dn, @su, @la);
                        ";
                        insertCmd.Parameters.AddWithValue("@d", record.DateString);
                        insertCmd.Parameters.AddWithValue("@c", record.ChildId);
                        insertCmd.Parameters.AddWithValue("@pn", app.ProcessName.ToLowerInvariant());
                        insertCmd.Parameters.AddWithValue("@dn", app.DisplayName);
                        insertCmd.Parameters.AddWithValue("@su", app.SecondsUsed);
                        insertCmd.Parameters.AddWithValue("@la", app.LastActive.ToString("o"));
                        insertCmd.ExecuteNonQuery();
                    }
                }

                trans.Commit();
            }
        }

        public List<DailyUsageRecord> GetUsageForDateRange(DateTime start, DateTime end, string childId = "default")
        {
            lock (_dbLock)
            {
                var list = new List<DailyUsageRecord>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT date, total_seconds, bonus_seconds, is_locked FROM daily_usage WHERE date >= @s AND date <= @e AND child_id = @c ORDER BY date ASC;";
                cmd.Parameters.AddWithValue("@s", start.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@e", end.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@c", childId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new DailyUsageRecord
                    {
                        DateString = reader.GetString(0),
                        ChildId = childId,
                        TotalDeviceSecondsUsed = reader.GetInt64(1),
                        ManualBonusSeconds = reader.GetInt64(2),
                        IsDeviceLocked = reader.GetInt32(3) == 1
                    });
                }

                return list;
            }
        }

        // Requests
        public List<ChildRequest> GetRequests(RequestStatus? statusFilter = null)
        {
            lock (_dbLock)
            {
                var list = new List<ChildRequest>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                if (statusFilter.HasValue)
                {
                    cmd.CommandText = "SELECT id, child_name, type, title, reason, requested_minutes, target_process, status, created_at, responded_at FROM requests WHERE status = @s ORDER BY created_at DESC;";
                    cmd.Parameters.AddWithValue("@s", statusFilter.Value.ToString());
                }
                else
                {
                    cmd.CommandText = "SELECT id, child_name, type, title, reason, requested_minutes, target_process, status, created_at, responded_at FROM requests ORDER BY created_at DESC;";
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ChildRequest
                    {
                        Id = reader.GetString(0),
                        ChildName = reader.GetString(1),
                        Type = Enum.TryParse<RequestType>(reader.GetString(2), out var t) ? t : RequestType.ExtraScreenTime,
                        Title = reader.GetString(3),
                        Reason = reader.GetString(4),
                        RequestedMinutes = reader.GetInt32(5),
                        TargetProcessName = reader.GetString(6),
                        Status = Enum.TryParse<RequestStatus>(reader.GetString(7), out var s) ? s : RequestStatus.Pending,
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var c) ? c : DateTime.Now,
                        RespondedAt = reader.IsDBNull(9) ? null : (DateTime.TryParse(reader.GetString(9), out var r) ? r : null)
                    });
                }

                return list;
            }
        }

        public void SaveRequest(ChildRequest req)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO requests (id, child_name, type, title, reason, requested_minutes, target_process, status, created_at, responded_at)
                    VALUES (@id, @cn, @t, @tit, @r, @rm, @tp, @s, @ca, @ra)
                    ON CONFLICT(id) DO UPDATE SET
                        status = @s,
                        responded_at = @ra;
                ";
                cmd.Parameters.AddWithValue("@id", req.Id);
                cmd.Parameters.AddWithValue("@cn", req.ChildName ?? "Alex");
                cmd.Parameters.AddWithValue("@t", req.Type.ToString());
                cmd.Parameters.AddWithValue("@tit", req.Title);
                cmd.Parameters.AddWithValue("@r", req.Reason);
                cmd.Parameters.AddWithValue("@rm", req.RequestedMinutes);
                cmd.Parameters.AddWithValue("@tp", req.TargetProcessName ?? "");
                cmd.Parameters.AddWithValue("@s", req.Status.ToString());
                cmd.Parameters.AddWithValue("@ca", req.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@ra", req.RespondedAt.HasValue ? req.RespondedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // Incidents
        public void LogIncident(string eventType, string processName, string description)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO incidents (timestamp, event_type, process_name, description) VALUES (@ts, @et, @pn, @desc);";
                cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("o"));
                cmd.Parameters.AddWithValue("@et", eventType);
                cmd.Parameters.AddWithValue("@pn", processName);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.ExecuteNonQuery();
            }
        }

        public List<(DateTime Timestamp, string EventType, string ProcessName, string Description)> GetIncidents(int limit = 50)
        {
            lock (_dbLock)
            {
                var list = new List<(DateTime, string, string, string)>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT timestamp, event_type, process_name, description FROM incidents ORDER BY id DESC LIMIT @lim;";
                cmd.Parameters.AddWithValue("@lim", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    DateTime ts = DateTime.TryParse(reader.GetString(0), out var dt) ? dt : DateTime.Now;
                    list.Add((ts, reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }

                return list;
            }
        }

        // Notifications
        public List<GuardianNotification> GetNotifications(int limit = 30)
        {
            lock (_dbLock)
            {
                var list = new List<GuardianNotification>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, title, message, severity, is_read, timestamp FROM notifications ORDER BY timestamp DESC LIMIT @lim;";
                cmd.Parameters.AddWithValue("@lim", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new GuardianNotification
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Message = reader.GetString(2),
                        Severity = Enum.TryParse<NotificationSeverity>(reader.GetString(3), out var s) ? s : NotificationSeverity.Info,
                        IsRead = reader.GetInt32(4) == 1,
                        Timestamp = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.Now
                    });
                }
                return list;
            }
        }

        public int GetUnreadNotificationCount()
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM notifications WHERE is_read = 0;";
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
        }

        public void SaveNotification(GuardianNotification notif)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO notifications (id, title, message, severity, is_read, timestamp)
                    VALUES (@id, @t, @m, @s, @r, @ts)
                    ON CONFLICT(id) DO UPDATE SET is_read = @r;
                ";
                cmd.Parameters.AddWithValue("@id", notif.Id);
                cmd.Parameters.AddWithValue("@t", notif.Title);
                cmd.Parameters.AddWithValue("@m", notif.Message);
                cmd.Parameters.AddWithValue("@s", notif.Severity.ToString());
                cmd.Parameters.AddWithValue("@r", notif.IsRead ? 1 : 0);
                cmd.Parameters.AddWithValue("@ts", notif.Timestamp.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkAllNotificationsRead()
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE notifications SET is_read = 1;";
                cmd.ExecuteNonQuery();
            }
        }

        // --- Remote Commands ---
        public void SaveRemoteCommand(RemoteCommand command)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO RemoteCommands (id, child_id, device_id, command_type, payload_json, status, created_by, created_at, executed_at, error_message)
                    VALUES (@id, @child_id, @device_id, @command_type, @payload_json, @status, @created_by, @created_at, @executed_at, @error_message)
                    ON CONFLICT(id) DO UPDATE SET
                        status = @status,
                        executed_at = @executed_at,
                        error_message = @error_message;
                ";
                cmd.Parameters.AddWithValue("@id", command.Id);
                cmd.Parameters.AddWithValue("@child_id", command.ChildId);
                cmd.Parameters.AddWithValue("@device_id", command.DeviceId);
                cmd.Parameters.AddWithValue("@command_type", command.CommandType.ToString());
                cmd.Parameters.AddWithValue("@payload_json", command.PayloadJson ?? "{}");
                cmd.Parameters.AddWithValue("@status", command.Status.ToString());
                cmd.Parameters.AddWithValue("@created_by", command.CreatedBy);
                cmd.Parameters.AddWithValue("@created_at", command.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@executed_at", command.ExecutedAt.HasValue ? command.ExecutedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@error_message", command.ErrorMessage ?? string.Empty);
                cmd.ExecuteNonQuery();
            }
        }

        public RemoteCommand? GetRemoteCommand(string id)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, child_id, device_id, command_type, payload_json, status, created_by, created_at, executed_at, error_message FROM RemoteCommands WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return ParseRemoteCommand(reader);
                }
                return null;
            }
        }

        public List<RemoteCommand> GetPendingCommandsForDevice(string deviceId)
        {
            var list = new List<RemoteCommand>();
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, child_id, device_id, command_type, payload_json, status, created_by, created_at, executed_at, error_message 
                    FROM RemoteCommands 
                    WHERE (device_id = @device_id OR device_id = '*') AND status IN ('Pending', 'Delivered')
                    ORDER BY created_at ASC;";
                cmd.Parameters.AddWithValue("@device_id", deviceId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseRemoteCommand(reader));
                }
            }
            return list;
        }

        public void UpdateCommandStatus(string id, CommandStatus status, string? errorMessage = null)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE RemoteCommands 
                    SET status = @status, 
                        executed_at = CASE WHEN @status IN ('Executed', 'Failed') THEN @now ELSE executed_at END,
                        error_message = COALESCE(@error_message, error_message)
                    WHERE id = @id;";
                cmd.Parameters.AddWithValue("@status", status.ToString());
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@error_message", (object?)errorMessage ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public List<RemoteCommand> GetRecentCommands(int limit = 50)
        {
            var list = new List<RemoteCommand>();
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, child_id, device_id, command_type, payload_json, status, created_by, created_at, executed_at, error_message 
                    FROM RemoteCommands 
                    ORDER BY created_at DESC 
                    LIMIT @limit;";
                cmd.Parameters.AddWithValue("@limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseRemoteCommand(reader));
                }
            }
            return list;
        }

        private RemoteCommand ParseRemoteCommand(SqliteDataReader reader)
        {
            Enum.TryParse<RemoteCommandType>(reader.GetString(3), out var type);
            Enum.TryParse<CommandStatus>(reader.GetString(5), out var status);
            return new RemoteCommand
            {
                Id = reader.GetString(0),
                ChildId = reader.GetString(1),
                DeviceId = reader.GetString(2),
                CommandType = type,
                PayloadJson = reader.IsDBNull(4) ? "{}" : reader.GetString(4),
                Status = status,
                CreatedBy = reader.IsDBNull(6) ? "Parent" : reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                ExecutedAt = !reader.IsDBNull(8) && DateTime.TryParse(reader.GetString(8), out var ea) ? ea : null,
                ErrorMessage = reader.IsDBNull(9) ? "" : reader.GetString(9)
            };
        }

        // --- Device Statuses ---
        public void SaveDeviceStatus(DeviceStatusRecord status)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO DeviceStatuses (device_id, device_name, child_id, is_online, last_seen, is_guardian_running, is_protected, screen_time_used_seconds, current_app, afk_active, parent_id)
                    VALUES (@device_id, @device_name, @child_id, @is_online, @last_seen, @is_guardian_running, @is_protected, @screen_time_used_seconds, @current_app, @afk_active, @parent_id)
                    ON CONFLICT(device_id) DO UPDATE SET
                        device_name = @device_name,
                        child_id = @child_id,
                        is_online = @is_online,
                        last_seen = @last_seen,
                        is_guardian_running = @is_guardian_running,
                        is_protected = @is_protected,
                        screen_time_used_seconds = @screen_time_used_seconds,
                        current_app = @current_app,
                        afk_active = @afk_active,
                        parent_id = @parent_id;
                ";
                cmd.Parameters.AddWithValue("@device_id", status.DeviceId);
                cmd.Parameters.AddWithValue("@device_name", status.DeviceName);
                cmd.Parameters.AddWithValue("@child_id", status.ChildId);
                cmd.Parameters.AddWithValue("@is_online", status.IsOnline ? 1 : 0);
                cmd.Parameters.AddWithValue("@last_seen", status.LastSeen.ToString("o"));
                cmd.Parameters.AddWithValue("@is_guardian_running", status.IsGuardianRunning ? 1 : 0);
                cmd.Parameters.AddWithValue("@is_protected", status.IsProtected ? 1 : 0);
                cmd.Parameters.AddWithValue("@screen_time_used_seconds", status.ScreenTimeUsedSeconds);
                cmd.Parameters.AddWithValue("@current_app", status.CurrentApp ?? "Idle");
                cmd.Parameters.AddWithValue("@afk_active", status.AfkActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@parent_id", status.ParentId ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public DeviceStatusRecord? GetDeviceStatus(string deviceId)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT device_id, device_name, child_id, is_online, last_seen, is_guardian_running, is_protected, screen_time_used_seconds, current_app, afk_active FROM DeviceStatuses WHERE device_id = @device_id;";
                cmd.Parameters.AddWithValue("@device_id", deviceId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return ParseDeviceStatusRecord(reader);
                }
                return null;
            }
        }

        public List<DeviceStatusRecord> GetAllDeviceStatuses()
        {
            var list = new List<DeviceStatusRecord>();
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT device_id, device_name, child_id, is_online, last_seen, is_guardian_running, is_protected, screen_time_used_seconds, current_app, afk_active FROM DeviceStatuses ORDER BY last_seen DESC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseDeviceStatusRecord(reader));
                }
            }
            return list;
        }

        private DeviceStatusRecord ParseDeviceStatusRecord(SqliteDataReader reader)
        {
            DateTime lastSeen = DateTime.TryParse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var ls)
                ? (ls.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(ls, DateTimeKind.Utc) : ls.ToUniversalTime())
                : DateTime.UtcNow;
            bool isOnline = (DateTime.UtcNow - lastSeen).TotalSeconds < 30;

            return new DeviceStatusRecord
            {
                DeviceId = reader.GetString(0),
                DeviceName = reader.GetString(1),
                ChildId = reader.GetString(2),
                IsOnline = isOnline,
                LastSeen = lastSeen,
                IsGuardianRunning = reader.GetInt32(5) == 1,
                IsProtected = reader.GetInt32(6) == 1,
                ScreenTimeUsedSeconds = reader.GetInt64(7),
                CurrentApp = reader.IsDBNull(8) ? "Idle" : reader.GetString(8),
                AfkActive = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                ParentId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : ""
            };
        }

        private void EnsureUserAccountsTablesExist(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS UserAccounts (
                    id TEXT PRIMARY KEY,
                    username TEXT NOT NULL,
                    normalized_username TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    password_salt TEXT NOT NULL,
                    role TEXT NOT NULL DEFAULT 'Parent',
                    parent_id TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS user_accounts (
                    id TEXT PRIMARY KEY,
                    username TEXT NOT NULL,
                    normalized_username TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    password_salt TEXT NOT NULL,
                    role TEXT NOT NULL DEFAULT 'Parent',
                    parent_id TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1
                );
            ";
            try { cmd.ExecuteNonQuery(); } catch { }

            string[] tables = new[] { "UserAccounts", "user_accounts" };
            foreach (var tbl in tables)
            {
                var cols = new[]
                {
                    ("normalized_username", "TEXT NOT NULL DEFAULT ''"),
                    ("password_hash", "TEXT NOT NULL DEFAULT ''"),
                    ("password_salt", "TEXT NOT NULL DEFAULT ''"),
                    ("role", "TEXT NOT NULL DEFAULT 'Parent'"),
                    ("parent_id", "TEXT NOT NULL DEFAULT ''"),
                    ("created_at", "TEXT NOT NULL DEFAULT ''"),
                    ("is_active", "INTEGER NOT NULL DEFAULT 1")
                };

                foreach (var (col, colDef) in cols)
                {
                    try
                    {
                        using var alterCmd = conn.CreateCommand();
                        alterCmd.CommandText = $"ALTER TABLE {tbl} ADD COLUMN {col} {colDef};";
                        alterCmd.ExecuteNonQuery();
                    }
                    catch { /* Column already exists */ }
                }
            }
        }

        public UserAccount? GetUserByUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            string norm = username.Trim().ToUpperInvariant();
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                EnsureUserAccountsTablesExist(conn);

                string[] tables = new[] { "UserAccounts", "user_accounts" };
                foreach (var table in tables)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT id, username, normalized_username, password_hash, password_salt, role, parent_id, created_at, is_active FROM {table} WHERE normalized_username = @norm OR UPPER(username) = @norm;";
                        cmd.Parameters.AddWithValue("@norm", norm);
                        using var reader = cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            Enum.TryParse<UserRole>(reader.GetString(5), true, out var role);
                            return new UserAccount
                            {
                                Id = reader.GetString(0),
                                Username = reader.GetString(1),
                                NormalizedUsername = reader.GetString(2),
                                PasswordHash = reader.GetString(3),
                                PasswordSalt = reader.GetString(4),
                                Role = role,
                                ParentId = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                                IsActive = reader.GetInt32(8) == 1
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"GetUserByUsername error on table {table}: {ex.Message}", ex);
                    }
                }
            }
            return null;
        }

        public UserAccount? GetUserById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                EnsureUserAccountsTablesExist(conn);

                string[] tables = new[] { "UserAccounts", "user_accounts" };
                foreach (var table in tables)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT id, username, normalized_username, password_hash, password_salt, role, parent_id, created_at, is_active FROM {table} WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using var reader = cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            Enum.TryParse<UserRole>(reader.GetString(5), true, out var role);
                            return new UserAccount
                            {
                                Id = reader.GetString(0),
                                Username = reader.GetString(1),
                                NormalizedUsername = reader.GetString(2),
                                PasswordHash = reader.GetString(3),
                                PasswordSalt = reader.GetString(4),
                                Role = role,
                                ParentId = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                                IsActive = reader.GetInt32(8) == 1
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"GetUserById error on table {table}: {ex.Message}", ex);
                    }
                }
            }
            return null;
        }

        public void SaveUserAccount(UserAccount user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Username)) return;
            string norm = user.Username.Trim().ToUpperInvariant();
            user.NormalizedUsername = norm;

            // Check if user with this username exists to keep identical Id across updates
            var existing = GetUserByUsername(user.Username);
            if (existing != null && existing.Id != user.Id)
            {
                user.Id = existing.Id;
            }

            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                EnsureUserAccountsTablesExist(conn);

                string[] tables = new[] { "UserAccounts", "user_accounts" };
                foreach (var table in tables)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $@"
                            INSERT INTO {table} (id, username, normalized_username, password_hash, password_salt, role, parent_id, created_at, is_active)
                            VALUES (@id, @user, @norm, @hash, @salt, @role, @parent, @created, @active)
                            ON CONFLICT(id) DO UPDATE SET
                                username = excluded.username,
                                normalized_username = excluded.normalized_username,
                                password_hash = excluded.password_hash,
                                password_salt = excluded.password_salt,
                                role = excluded.role,
                                parent_id = excluded.parent_id,
                                is_active = excluded.is_active;
                        ";
                        cmd.Parameters.AddWithValue("@id", user.Id);
                        cmd.Parameters.AddWithValue("@user", user.Username.Trim());
                        cmd.Parameters.AddWithValue("@norm", norm);
                        cmd.Parameters.AddWithValue("@hash", user.PasswordHash);
                        cmd.Parameters.AddWithValue("@salt", user.PasswordSalt);
                        cmd.Parameters.AddWithValue("@role", user.Role.ToString());
                        cmd.Parameters.AddWithValue("@parent", user.ParentId ?? "");
                        cmd.Parameters.AddWithValue("@created", user.CreatedAt.ToString("o"));
                        cmd.Parameters.AddWithValue("@active", user.IsActive ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"SaveUserAccount error on table {table}: {ex.Message}", ex);
                    }
                }
            }
        }

        public List<ChildProfile> GetChildrenForParent(string parentId)
        {
            var list = new List<ChildProfile>();
            if (string.IsNullOrWhiteSpace(parentId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                // Select child profiles where there is an ACTIVE ParentChildRelationship for parentId
                cmd.CommandText = @"
                    SELECT id, name, avatar, age, is_active, daily_limit_minutes, bedtime, bedtime_enabled, created_at, parent_id, user_id 
                    FROM children 
                    WHERE (user_id IN (SELECT child_user_id FROM ParentChildRelationships WHERE parent_user_id = @pid AND is_active = 1)
                           OR parent_id = @pid)
                      AND EXISTS (SELECT 1 FROM ParentChildRelationships WHERE parent_user_id = @pid AND is_active = 1)
                    ORDER BY name;
                ";
                cmd.Parameters.AddWithValue("@pid", parentId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ChildProfile
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Avatar = reader.GetString(2),
                        Age = reader.GetInt32(3),
                        IsActive = reader.GetInt32(4) == 1,
                        DailyLimitMinutes = reader.GetInt32(5),
                        Bedtime = TimeSpan.TryParse(reader.GetString(6), out var bt) ? bt : new TimeSpan(21, 30, 0),
                        BedtimeEnabled = reader.GetInt32(7) == 1,
                        CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca : DateTime.Now,
                        ParentId = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        UserId = reader.IsDBNull(10) ? "" : reader.GetString(10)
                    });
                }
            }
            return list;
        }

        public List<DeviceStatusRecord> GetDevicesForParent(string parentId)
        {
            var list = new List<DeviceStatusRecord>();
            if (string.IsNullOrWhiteSpace(parentId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                // STRICT DEVICE VISIBILITY: Devices are returned ONLY if child_id (or parent_id) is connected via an active ParentChildRelationship
                cmd.CommandText = @"
                    SELECT device_id, device_name, child_id, is_online, last_seen, is_guardian_running, is_protected, screen_time_used_seconds, current_app, afk_active, parent_id 
                    FROM DeviceStatuses 
                    WHERE (
                            child_id IN (SELECT child_user_id FROM ParentChildRelationships WHERE parent_user_id = @pid AND is_active = 1)
                            OR parent_id IN (SELECT child_user_id FROM ParentChildRelationships WHERE parent_user_id = @pid AND is_active = 1)
                          )
                      AND EXISTS (SELECT 1 FROM ParentChildRelationships WHERE parent_user_id = @pid AND is_active = 1)
                    ORDER BY last_seen DESC;
                ";
                cmd.Parameters.AddWithValue("@pid", parentId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseDeviceStatusRecord(reader));
                }
            }
            return list;
        }

        public List<DeviceStatusRecord> GetDevicesForChild(string childId)
        {
            var list = new List<DeviceStatusRecord>();
            if (string.IsNullOrWhiteSpace(childId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT device_id, device_name, child_id, is_online, last_seen, is_guardian_running, is_protected, screen_time_used_seconds, current_app, afk_active, parent_id FROM DeviceStatuses WHERE child_id = @cid OR parent_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", childId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseDeviceStatusRecord(reader));
                }
            }
            return list;
        }

        // --- ParentChildInvitations & ParentChildRelationships Implementation ---
        public void SaveInvitation(ParentChildInvitation inv)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ParentChildInvitations (id, parent_user_id, parent_username, child_user_id, child_username, status, created_at, responded_at, expires_at)
                    VALUES (@id, @puid, @pname, @cuid, @cname, @status, @created, @responded, @expires)
                    ON CONFLICT(id) DO UPDATE SET
                        status = excluded.status,
                        responded_at = excluded.responded_at;
                ";
                cmd.Parameters.AddWithValue("@id", inv.Id);
                cmd.Parameters.AddWithValue("@puid", inv.ParentUserId);
                cmd.Parameters.AddWithValue("@pname", inv.ParentUsername);
                cmd.Parameters.AddWithValue("@cuid", inv.ChildUserId);
                cmd.Parameters.AddWithValue("@cname", inv.ChildUsername);
                cmd.Parameters.AddWithValue("@status", inv.Status.ToString());
                cmd.Parameters.AddWithValue("@created", inv.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@responded", inv.RespondedAt.HasValue ? inv.RespondedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@expires", inv.ExpiresAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public ParentChildInvitation? GetInvitation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, parent_user_id, parent_username, child_user_id, child_username, status, created_at, responded_at, expires_at FROM ParentChildInvitations WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return ParseInvitationRecord(reader);
                }
            }
            return null;
        }

        public List<ParentChildInvitation> GetInvitationsForParent(string parentUserId)
        {
            var list = new List<ParentChildInvitation>();
            if (string.IsNullOrWhiteSpace(parentUserId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                // Auto-expire old pending invitations
                using var expireCmd = conn.CreateCommand();
                expireCmd.CommandText = "UPDATE ParentChildInvitations SET status = 'Expired' WHERE status = 'Pending' AND expires_at < @now;";
                expireCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                expireCmd.ExecuteNonQuery();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, parent_user_id, parent_username, child_user_id, child_username, status, created_at, responded_at, expires_at FROM ParentChildInvitations WHERE parent_user_id = @puid ORDER BY created_at DESC;";
                cmd.Parameters.AddWithValue("@puid", parentUserId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseInvitationRecord(reader));
                }
            }
            return list;
        }

        public List<ParentChildInvitation> GetPendingInvitationsForChild(string childUserId)
        {
            var list = new List<ParentChildInvitation>();
            if (string.IsNullOrWhiteSpace(childUserId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                // Auto-expire old pending invitations
                using var expireCmd = conn.CreateCommand();
                expireCmd.CommandText = "UPDATE ParentChildInvitations SET status = 'Expired' WHERE status = 'Pending' AND expires_at < @now;";
                expireCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                expireCmd.ExecuteNonQuery();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, parent_user_id, parent_username, child_user_id, child_username, status, created_at, responded_at, expires_at FROM ParentChildInvitations WHERE child_user_id = @cuid AND status = 'Pending' AND expires_at > @now ORDER BY created_at DESC;";
                cmd.Parameters.AddWithValue("@cuid", childUserId);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ParseInvitationRecord(reader));
                }
            }
            return list;
        }

        public void UpdateInvitationStatus(string id, InvitationStatus status, DateTime? respondedAt = null)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE ParentChildInvitations SET status = @status, responded_at = @responded WHERE id = @id;";
                cmd.Parameters.AddWithValue("@status", status.ToString());
                cmd.Parameters.AddWithValue("@responded", respondedAt.HasValue ? respondedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveRelationship(ParentChildRelationship rel)
        {
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ParentChildRelationships (id, parent_user_id, child_user_id, created_at, is_active)
                    VALUES (@id, @puid, @cuid, @created, @active)
                    ON CONFLICT(id) DO UPDATE SET is_active = excluded.is_active;
                ";
                cmd.Parameters.AddWithValue("@id", rel.Id);
                cmd.Parameters.AddWithValue("@puid", rel.ParentUserId);
                cmd.Parameters.AddWithValue("@cuid", rel.ChildUserId);
                cmd.Parameters.AddWithValue("@created", rel.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@active", rel.IsActive ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        public List<ParentChildRelationship> GetActiveRelationshipsForParent(string parentUserId)
        {
            var list = new List<ParentChildRelationship>();
            if (string.IsNullOrWhiteSpace(parentUserId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, parent_user_id, child_user_id, created_at, is_active FROM ParentChildRelationships WHERE parent_user_id = @puid AND is_active = 1;";
                cmd.Parameters.AddWithValue("@puid", parentUserId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ParentChildRelationship
                    {
                        Id = reader.GetString(0),
                        ParentUserId = reader.GetString(1),
                        ChildUserId = reader.GetString(2),
                        CreatedAt = DateTime.TryParse(reader.GetString(3), out var ca) ? ca : DateTime.UtcNow,
                        IsActive = reader.GetInt32(4) == 1
                    });
                }
            }
            return list;
        }

        public List<ParentChildRelationship> GetActiveRelationshipsForChild(string childUserId)
        {
            var list = new List<ParentChildRelationship>();
            if (string.IsNullOrWhiteSpace(childUserId)) return list;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, parent_user_id, child_user_id, created_at, is_active FROM ParentChildRelationships WHERE child_user_id = @cuid AND is_active = 1;";
                cmd.Parameters.AddWithValue("@cuid", childUserId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ParentChildRelationship
                    {
                        Id = reader.GetString(0),
                        ParentUserId = reader.GetString(1),
                        ChildUserId = reader.GetString(2),
                        CreatedAt = DateTime.TryParse(reader.GetString(3), out var ca) ? ca : DateTime.UtcNow,
                        IsActive = reader.GetInt32(4) == 1
                    });
                }
            }
            return list;
        }

        public bool IsParentConnectedToChild(string parentUserId, string childUserId)
        {
            if (string.IsNullOrWhiteSpace(parentUserId) || string.IsNullOrWhiteSpace(childUserId)) return false;
            lock (_dbLock)
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM ParentChildRelationships WHERE parent_user_id = @puid AND child_user_id = @cuid AND is_active = 1;";
                cmd.Parameters.AddWithValue("@puid", parentUserId);
                cmd.Parameters.AddWithValue("@cuid", childUserId);
                long count = (long)(cmd.ExecuteScalar() ?? 0L);
                return count > 0;
            }
        }

        private ParentChildInvitation ParseInvitationRecord(SqliteDataReader reader)
        {
            Enum.TryParse<InvitationStatus>(reader.GetString(5), true, out var status);
            return new ParentChildInvitation
            {
                Id = reader.GetString(0),
                ParentUserId = reader.GetString(1),
                ParentUsername = reader.GetString(2),
                ChildUserId = reader.GetString(3),
                ChildUsername = reader.GetString(4),
                Status = status,
                CreatedAt = DateTime.TryParse(reader.GetString(6), out var ca) ? ca : DateTime.UtcNow,
                RespondedAt = reader.IsDBNull(7) ? null : (DateTime.TryParse(reader.GetString(7), out var ra) ? ra : null),
                ExpiresAt = DateTime.TryParse(reader.GetString(8), out var ea) ? ea : DateTime.UtcNow.AddDays(7)
            };
        }
    }
}
