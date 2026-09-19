using System;
using System.IO;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Xunit;

namespace Guardian.Tests
{
    public class GuardianCoreTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly IDatabaseService _db;
        private readonly IAuthenticationService _auth;

        public GuardianCoreTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"guardian_test_{Guid.NewGuid():N}.db");
            _db = new DatabaseService(_testDbPath);
            _auth = new AuthenticationService(_db);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch { }
        }

        [Fact]
        public void Test1_Authentication_DefaultPassword_1111_Succeeds()
        {
            Assert.True(_auth.IsDefaultPassword);
            Assert.True(_auth.VerifyPassword("1111"));
            Assert.True(_auth.AuthenticateAndStartSession("1111"));
            Assert.True(_auth.HasAuthenticatedSession);
        }

        [Fact]
        public void Test2_Authentication_IncorrectPassword_Fails()
        {
            Assert.False(_auth.VerifyPassword("wrong_password"));
            Assert.False(_auth.VerifyPassword("0000"));
            Assert.False(_auth.AuthenticateAndStartSession("invalid"));
        }

        [Fact]
        public void Test3_Authentication_SetNewPassword_And_Invalidate_Old()
        {
            bool changed = _auth.SetNewPassword("1111", "SecretPass123", out string err);
            Assert.True(changed, err);
            Assert.False(_auth.IsDefaultPassword);

            Assert.True(_auth.VerifyPassword("SecretPass123"));
            Assert.False(_auth.VerifyPassword("1111"));
        }

        [Fact]
        public void Test4_AdvancedSchedules_Overrides_And_Bedtime()
        {
            var sched = new AdvancedDaySchedule
            {
                Day = DayOfWeek.Wednesday,
                LimitMinutes = 60,
                HasAllowedHours = true,
                AllowedStartTime = new TimeSpan(16, 0, 0),
                AllowedEndTime = new TimeSpan(18, 0, 0),
                BedtimeEnabled = true,
                Bedtime = new TimeSpan(21, 0, 0)
            };

            _db.SaveAdvancedSchedule(sched);

            var loaded = _db.GetAdvancedSchedule(DayOfWeek.Wednesday);
            Assert.Equal(60, loaded.LimitMinutes);
            Assert.True(loaded.HasAllowedHours);
            Assert.Equal(new TimeSpan(21, 0, 0), loaded.Bedtime);
        }

        [Fact]
        public void Test5_DailyUsage_Persistence_And_Bonus_Time()
        {
            DateTime today = DateTime.Today;
            var record = _db.GetOrCreateDailyUsage(today, "child_1");
            record.TotalDeviceSecondsUsed = 3600;
            record.ManualBonusSeconds = 1800;
            record.AppUsages["robloxplayerbeta"] = new AppUsageEntry
            {
                ProcessName = "robloxplayerbeta",
                DisplayName = "Roblox",
                SecondsUsed = 1200,
                LastActive = DateTime.Now
            };

            _db.SaveDailyUsage(record);

            var reloaded = _db.GetOrCreateDailyUsage(today, "child_1");
            Assert.Equal(3600, reloaded.TotalDeviceSecondsUsed);
            Assert.Equal(1800, reloaded.ManualBonusSeconds);
            Assert.True(reloaded.AppUsages.ContainsKey("robloxplayerbeta"));
            Assert.Equal(1200, reloaded.AppUsages["robloxplayerbeta"].SecondsUsed);
        }

        [Fact]
        public void Test6_AppRules_CRUD_And_Blocking()
        {
            var rule = new AppRule
            {
                ProcessName = "customgame",
                DisplayName = "Custom Game",
                IsBlocked = true,
                DailyLimitMinutes = 30,
                Category = "Games"
            };

            _db.SaveAppRule(rule);

            var loaded = _db.GetAppRule("customgame");
            Assert.NotNull(loaded);
            Assert.True(loaded.IsBlocked);
            Assert.Equal(30, loaded.DailyLimitMinutes);

            loaded.IsBlocked = false;
            _db.SaveAppRule(loaded);

            var updated = _db.GetAppRule("customgame");
            Assert.NotNull(updated);
            Assert.False(updated.IsBlocked);
        }

        [Fact]
        public void Test7_WebFilter_Rules_CRUD()
        {
            var webRule = new WebFilterRule
            {
                Domain = "tiktok.com",
                Category = "Social Media",
                IsBlocked = true,
                Reason = "Parental Block"
            };

            _db.SaveWebRule(webRule);

            var list = _db.GetWebRules();
            Assert.Contains(list, r => r.Domain == "tiktok.com" && r.IsBlocked);

            _db.DeleteWebRule("tiktok.com");
            var listAfter = _db.GetWebRules();
            Assert.DoesNotContain(listAfter, r => r.Domain == "tiktok.com");
        }

        [Fact]
        public void Test8_ChildProfiles_CRUD()
        {
            var child = new ChildProfile
            {
                Name = "Oliver",
                Avatar = "🧒",
                Age = 9,
                DailyLimitMinutes = 90
            };

            _db.SaveChild(child);

            var loaded = _db.GetChild(child.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Oliver", loaded.Name);
            Assert.Equal(90, loaded.DailyLimitMinutes);
        }

        [Fact]
        public void Test9_DatabaseSchema_Contains_All_16_Required_Tables()
        {
            string[] requiredTables = new[]
            {
                "Users", "ParentAuthentication", "Children", "AppRules", "AppUsage",
                "DailyUsage", "Schedules", "ScheduleDays", "WebsiteRules", "TimeRequests",
                "Notifications", "ActivityLog", "DeviceState", "ProtectionState", "Settings", "AuditLog"
            };

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_testDbPath};");
            conn.Open();

            foreach (var table in requiredTables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND lower(name)=lower(@tbl);";
                cmd.Parameters.AddWithValue("@tbl", table);
                long count = (long)(cmd.ExecuteScalar() ?? 0L);
                Assert.True(count > 0, $"Required table '{table}' is missing from SQLite database schema!");
            }
        }

        [Fact]
        public void Test10_TimeProtectionService_MonotonicVerification()
        {
            var timeProtection = new TimeProtectionService(_db);
            timeProtection.VerifySystemClock();
            Assert.False(timeProtection.IsTimeTamperingDetected);
            Assert.True(timeProtection.LastVerifiedUtcTime <= DateTime.UtcNow);
        }

        [Fact]
        public void Test11_UsageTracking_ParentUnlockedForToday_State()
        {
            using var tracker = new UsageTrackingService(_db);
            Assert.False(tracker.CurrentDailyRecord.ParentUnlockedForToday);

            tracker.UnlockDeviceForToday();

            Assert.True(tracker.CurrentDailyRecord.ParentUnlockedForToday);
            Assert.False(tracker.IsDeviceLimitReached);
        }

        [Fact]
        public void Test12_AfkMode_Activation_Correct_And_Incorrect_Password()
        {
            using var tracker = new UsageTrackingService(_db);
            Assert.False(tracker.IsAfkModeActive);

            // Wrong password fails
            bool wrongRes = tracker.ActivateAfkMode("wrongpassword", 30);
            Assert.False(wrongRes);
            Assert.False(tracker.IsAfkModeActive);

            // Correct password succeeds
            bool correctRes = tracker.ActivateAfkMode("1111", 30);
            Assert.True(correctRes);
            Assert.True(tracker.IsAfkModeActive);
            Assert.NotNull(tracker.AfkStartedAt);

            // SQLite state verified
            var afkDbState = _db.LoadAfkState();
            Assert.True(afkDbState.IsAfkModeActive);
            Assert.Equal(30, afkDbState.AfkDurationMinutes);
        }

        [Fact]
        public void Test13_AfkMode_ManualDeactivation_RequiresPassword()
        {
            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 0);
            Assert.True(tracker.IsAfkModeActive);

            // Deactivate with wrong password fails
            bool wrongDeact = tracker.DeactivateAfkMode("wrongpassword");
            Assert.False(wrongDeact);
            Assert.True(tracker.IsAfkModeActive);

            // Deactivate with correct password succeeds
            bool correctDeact = tracker.DeactivateAfkMode("1111");
            Assert.True(correctDeact);
            Assert.False(tracker.IsAfkModeActive);

            var afkDbState = _db.LoadAfkState();
            Assert.False(afkDbState.IsAfkModeActive);
        }

        [Fact]
        public void Test14_AfkMode_Usage_And_App_Usage_Exclusion()
        {
            DateTime today = DateTime.Today;
            var record = _db.GetOrCreateDailyUsage(today, "default");
            record.TotalDeviceSecondsUsed = 4800; // 1h 20m
            record.TotalAfkSecondsUsed = 0;
            record.AppUsages["robloxplayerbeta"] = new AppUsageEntry
            {
                ProcessName = "robloxplayerbeta",
                DisplayName = "Roblox",
                SecondsUsed = 2700, // 45m
                LastActive = DateTime.Now
            };
            _db.SaveDailyUsage(record);

            using var tracker = new UsageTrackingService(_db);
            Assert.Equal(4800, tracker.TotalDeviceSecondsToday);

            // Activate AFK Mode (120 mins = 2 hours)
            tracker.ActivateAfkMode("1111", 120);

            // Record AFK usage into daily usage (simulating AFK interval)
            record = tracker.CurrentDailyRecord;
            record.TotalAfkSecondsUsed += 3600; // 1 hour AFK
            _db.SaveDailyUsage(record);

            // TotalDeviceSecondsUsed MUST NOT change (remains 4800s / 1h 20m)
            Assert.Equal(4800, tracker.TotalDeviceSecondsToday);
            // TotalAfkSecondsUsed MUST equal 3600s
            Assert.Equal(3600, tracker.TotalAfkSecondsToday);
            // App usage for Roblox MUST remain unchanged at 2700s
            Assert.Equal(2700, tracker.CurrentDailyRecord.AppUsages["robloxplayerbeta"].SecondsUsed);

            // Deactivate AFK
            tracker.DeactivateAfkMode("1111");
            Assert.Equal(4800, tracker.TotalDeviceSecondsToday);
        }

        [Fact]
        public void Test15_AfkMode_DurationExpiration_AutoDeactivates()
        {
            // Simulate AFK active for 30 mins, but started 40 mins ago
            var expiredState = new AfkState
            {
                IsAfkModeActive = true,
                AfkStartedAt = DateTime.Now.AddMinutes(-40),
                AfkDurationMinutes = 30,
                ActivatedBy = "Parent"
            };
            _db.SaveAfkState(expiredState);

            // Create new tracking service (startup recovery)
            using var tracker = new UsageTrackingService(_db);

            // Must auto-deactivate expired AFK mode on startup recovery
            Assert.False(tracker.IsAfkModeActive);
            var dbState = _db.LoadAfkState();
            Assert.False(dbState.IsAfkModeActive);
        }

        [Fact]
        public void Test16_AfkMode_PersistentSQLiteState_Restart_And_Watchdog_Recovery()
        {
            // Save active AFK state (started 10 mins ago for 60 mins)
            var activeState = new AfkState
            {
                IsAfkModeActive = true,
                AfkStartedAt = DateTime.Now.AddMinutes(-10),
                AfkDurationMinutes = 60,
                ActivatedBy = "Parent"
            };
            _db.SaveAfkState(activeState);

            // Simulate Watchdog or App Restart
            using var tracker = new UsageTrackingService(_db);

            Assert.True(tracker.IsAfkModeActive);
            Assert.Equal(60, tracker.AfkDurationMinutes);
            Assert.True(tracker.AfkTimeRemaining.TotalMinutes > 40 && tracker.AfkTimeRemaining.TotalMinutes <= 50);
        }

        [Fact]
        public void Test17_AfkMode_ProtectionRules_Schedules_AppLimits_And_Locks_RemainActive()
        {
            // 1. Bedtime Schedule Active
            var sched = new AdvancedDaySchedule
            {
                Day = DateTime.Today.DayOfWeek,
                LimitMinutes = 120,
                BedtimeEnabled = true,
                Bedtime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromMinutes(-5)) // 5 mins ago
            };
            _db.SaveAdvancedSchedule(sched);

            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 60);

            // Bedtime must still be detected as restricted!
            Assert.True(tracker.IsBedtimeRestricted);

            // 2. Blocked App Rule
            var blockedRule = new AppRule
            {
                ProcessName = "forbidden_app",
                DisplayName = "Forbidden App",
                IsBlocked = true
            };
            _db.SaveAppRule(blockedRule);
            var ruleLoaded = _db.GetAppRule("forbidden_app");
            Assert.NotNull(ruleLoaded);
            Assert.True(ruleLoaded.IsBlocked);

            // 3. Manual Device Lock
            tracker.LockDeviceManually("Parent Lock");
            Assert.True(tracker.IsDeviceLimitReached);
            Assert.True(tracker.CurrentDailyRecord.IsDeviceLocked);
        }

        [Fact]
        public void Test18_AfkMode_MidnightTransition_Preserves_AFK_State()
        {
            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 0);
            Assert.True(tracker.IsAfkModeActive);

            // Simulate rollover to next day
            _db.GetOrCreateDailyUsage(DateTime.Today.AddDays(1), "default");

            // AFK Mode status persists
            Assert.True(tracker.IsAfkModeActive);
        }

        [Fact]
        public void Test19_AfkMode_ClockTampering_Protected()
        {
            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 60);

            var timeProtection = new TimeProtectionService(_db);
            timeProtection.VerifySystemClock();
            Assert.False(timeProtection.IsTimeTamperingDetected);
        }

        [Fact]
        public void Test20_AfkLog_SessionCreatedOnActivation()
        {
            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 60);

            var active = _db.GetActiveAfkSession();
            Assert.NotNull(active);
            Assert.Null(active.EndTimestamp);
            Assert.Equal("Parent Password", active.ActivationMethod);
            Assert.Equal("Parent", active.ActivatedBy);
            Assert.Equal("Active", active.EndReason);
        }

        [Fact]
        public void Test21_AfkLog_SessionClosedOnDeactivation()
        {
            using var tracker = new UsageTrackingService(_db);
            tracker.ActivateAfkMode("1111", 60);
            var activeBefore = _db.GetActiveAfkSession();
            Assert.NotNull(activeBefore);

            tracker.DeactivateAfkMode("1111");

            var activeAfter = _db.GetActiveAfkSession();
            Assert.Null(activeAfter);

            var all = _db.GetAllAfkSessions();
            Assert.NotEmpty(all);
            var closedSession = all.First(s => s.Id == activeBefore.Id);
            Assert.NotNull(closedSession.EndTimestamp);
            Assert.Equal("Parent", closedSession.EndedBy);
            Assert.Equal("Parent Disabled", closedSession.EndReason);
        }

        [Fact]
        public void Test22_AfkLog_CrashRecovery_ResumesActiveSessionWithoutDuplicates()
        {
            var openSession = new AfkSessionRecord
            {
                Id = Guid.NewGuid().ToString(),
                StartTimestamp = DateTime.UtcNow.AddMinutes(-15),
                EndTimestamp = null,
                ActivationMethod = "Parent Password",
                ActivatedBy = "Parent",
                EndReason = "Active"
            };
            _db.SaveAfkSession(openSession);

            var afkState = new AfkState
            {
                IsAfkModeActive = true,
                AfkStartedAt = DateTime.Now.AddMinutes(-15),
                AfkDurationMinutes = 60,
                ActivatedBy = "Parent"
            };
            _db.SaveAfkState(afkState);

            // Recovery on start
            using var tracker = new UsageTrackingService(_db);
            Assert.True(tracker.IsAfkModeActive);

            var sessions = _db.GetAllAfkSessions();
            // Should NOT create a duplicate session
            Assert.Single(sessions);
            Assert.Equal(openSession.Id, sessions[0].Id);
            Assert.Null(sessions[0].EndTimestamp);
        }

        [Fact]
        public void Test23_AfkLog_CrashRecovery_ClosesExpiredSessionCorrectly()
        {
            var expiredSession = new AfkSessionRecord
            {
                Id = Guid.NewGuid().ToString(),
                StartTimestamp = DateTime.UtcNow.AddMinutes(-90),
                EndTimestamp = null,
                ActivationMethod = "Parent Password",
                ActivatedBy = "Parent",
                EndReason = "Active"
            };
            _db.SaveAfkSession(expiredSession);

            var afkState = new AfkState
            {
                IsAfkModeActive = true,
                AfkStartedAt = DateTime.Now.AddMinutes(-90),
                AfkDurationMinutes = 60, // 60 mins duration expired 30 mins ago
                ActivatedBy = "Parent"
            };
            _db.SaveAfkState(afkState);

            // Recovery on start
            using var tracker = new UsageTrackingService(_db);

            Assert.False(tracker.IsAfkModeActive);
            var activeSession = _db.GetActiveAfkSession();
            Assert.Null(activeSession);

            var all = _db.GetAllAfkSessions();
            Assert.NotEmpty(all);
            var closed = all.First(s => s.Id == expiredSession.Id);
            Assert.NotNull(closed.EndTimestamp);
            Assert.Equal("Duration Expired", closed.EndReason);
        }

        [Fact]
        public void Test24_AfkLog_SummaryStatsAndDateFiltering()
        {
            var s1 = new AfkSessionRecord
            {
                Id = Guid.NewGuid().ToString(),
                StartTimestamp = DateTime.UtcNow.AddHours(-1),
                EndTimestamp = DateTime.UtcNow,
                DurationSeconds = 3600,
                EndReason = "Parent Disabled"
            };
            var s2 = new AfkSessionRecord
            {
                Id = Guid.NewGuid().ToString(),
                StartTimestamp = DateTime.UtcNow.AddDays(-2),
                EndTimestamp = DateTime.UtcNow.AddDays(-2).AddHours(2),
                DurationSeconds = 7200,
                EndReason = "Duration Expired"
            };
            _db.SaveAfkSession(s1);
            _db.SaveAfkSession(s2);

            var stats = _db.GetAfkSummaryStats();
            Assert.True(stats.TotalCount >= 2);
            Assert.True(stats.TotalSeconds >= 10800);

            var all = _db.GetAllAfkSessions();
            Assert.Contains(all, s => s.Id == s1.Id);
            Assert.Contains(all, s => s.Id == s2.Id);
        }

        [Fact]
        public void Test25_RemoteCommands_Save_Get_UpdateStatus_PendingQueue()
        {
            var cmd = new RemoteCommand
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = "default",
                DeviceId = Environment.MachineName,
                CommandType = RemoteCommandType.LockDevice,
                Status = CommandStatus.Pending,
                CreatedBy = "Parent Web Dashboard",
                CreatedAt = DateTime.UtcNow
            };

            _db.SaveRemoteCommand(cmd);

            var loaded = _db.GetRemoteCommand(cmd.Id);
            Assert.NotNull(loaded);
            Assert.Equal(CommandStatus.Pending, loaded.Status);

            var pending = _db.GetPendingCommandsForDevice(Environment.MachineName);
            Assert.Contains(pending, c => c.Id == cmd.Id);

            _db.UpdateCommandStatus(cmd.Id, CommandStatus.Executed);

            var updated = _db.GetRemoteCommand(cmd.Id);
            Assert.NotNull(updated);
            Assert.Equal(CommandStatus.Executed, updated.Status);
            Assert.NotNull(updated.ExecutedAt);
        }

        [Fact]
        public void Test26_DeviceStatus_Save_Get_OnlineReporting()
        {
            var dev = new DeviceStatusRecord
            {
                DeviceId = "TEST_DEVICE_01",
                DeviceName = "Test Gaming PC",
                ChildId = "default",
                IsOnline = true,
                LastSeen = DateTime.UtcNow,
                IsGuardianRunning = true,
                IsProtected = true,
                ScreenTimeUsedSeconds = 3600,
                CurrentApp = "Roblox",
                AfkActive = false
            };

            _db.SaveDeviceStatus(dev);

            var loaded = _db.GetDeviceStatus("TEST_DEVICE_01");
            Assert.NotNull(loaded);
            Assert.Equal("Test Gaming PC", loaded.DeviceName);
            Assert.True(loaded.IsOnline);
            Assert.Equal("Roblox", loaded.CurrentApp);

            var all = _db.GetAllDeviceStatuses();
            Assert.Contains(all, d => d.DeviceId == "TEST_DEVICE_01");
        }

        [Fact]
        public async System.Threading.Tasks.Task Test27_DeviceSyncService_LocalExecution_LockDevice_And_AppBlock()
        {
            using var usageTracker = new UsageTrackingService(_db);
            using var enforcement = new EnforcementService(_db, usageTracker);
            using var syncService = new DeviceSyncService(_db, usageTracker, enforcement);

            var lockCmd = new RemoteCommand
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = "default",
                DeviceId = Environment.MachineName,
                CommandType = RemoteCommandType.LockDevice,
                Status = CommandStatus.Pending
            };

            await syncService.ExecuteCommandLocallyAsync(lockCmd);

            var executedCmd = _db.GetRemoteCommand(lockCmd.Id);
            Assert.NotNull(executedCmd);
            Assert.Equal(CommandStatus.Executed, executedCmd.Status);

            var blockCmd = new RemoteCommand
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = "default",
                DeviceId = Environment.MachineName,
                CommandType = RemoteCommandType.BlockApp,
                PayloadJson = "{\"processName\":\"testapp.exe\",\"displayName\":\"Test App\"}",
                Status = CommandStatus.Pending
            };

            await syncService.ExecuteCommandLocallyAsync(blockCmd);

            var appRule = _db.GetAppRule("testapp.exe");
            Assert.NotNull(appRule);
            Assert.True(appRule.IsBlocked);
        }

        [Fact]
        public void Test28_UserAccount_Registration_PBKDF2_And_Verification()
        {
            var user = _auth.RegisterUser("MasterParent", "SecurePass123!", UserRole.Parent);
            Assert.NotNull(user);
            Assert.Equal("MASTERPARENT", user.NormalizedUsername);
            Assert.NotEmpty(user.PasswordHash);
            Assert.NotEmpty(user.PasswordSalt);
            Assert.NotEqual("SecurePass123!", user.PasswordHash); // Password must be hashed

            var verified = _auth.VerifyUserCredentials("masterparent", "SecurePass123!");
            Assert.NotNull(verified);
            Assert.Equal(user.Id, verified.Id);

            var wrongPass = _auth.VerifyUserCredentials("masterparent", "WrongPassword");
            Assert.Null(wrongPass);

            var wrongUser = _auth.VerifyUserCredentials("NonExistentUser", "SecurePass123!");
            Assert.Null(wrongUser);
        }

        [Fact]
        public void Test29_ParentChild_Ownership_DataIsolation()
        {
            var parentA = _auth.RegisterUser("ParentA", "PassA123!", UserRole.Parent);
            var parentB = _auth.RegisterUser("ParentB", "PassB123!", UserRole.Parent);

            var childA = new ChildProfile { Id = "child_a", ParentId = parentA.Id, UserId = "user_a", Name = "ChildA" };
            var childB = new ChildProfile { Id = "child_b", ParentId = parentB.Id, UserId = "user_b", Name = "ChildB" };

            _db.SaveChild(childA);
            _db.SaveChild(childB);

            _db.SaveRelationship(new ParentChildRelationship { ParentUserId = parentA.Id, ChildUserId = "user_a", IsActive = true });
            _db.SaveRelationship(new ParentChildRelationship { ParentUserId = parentB.Id, ChildUserId = "user_b", IsActive = true });

            var parentAChildren = _db.GetChildrenForParent(parentA.Id);
            Assert.Single(parentAChildren);
            Assert.Equal("ChildA", parentAChildren[0].Name);

            var parentBChildren = _db.GetChildrenForParent(parentB.Id);
            Assert.Single(parentBChildren);
            Assert.Equal("ChildB", parentBChildren[0].Name);
        }

        [Fact]
        public void Test30_DeviceStatus_DynamicOnlineStatus_Heartbeat_Expiry()
        {
            string pid = "parent_01";
            string cid = "child_01";

            _db.SaveRelationship(new ParentChildRelationship { ParentUserId = pid, ChildUserId = cid, IsActive = true });

            var devRecent = new DeviceStatusRecord
            {
                DeviceId = "DEV_ONLINE_01",
                DeviceName = "Online PC",
                ParentId = pid,
                ChildId = cid,
                LastSeen = DateTime.UtcNow
            };
            _db.SaveDeviceStatus(devRecent);

            var devStale = new DeviceStatusRecord
            {
                DeviceId = "DEV_OFFLINE_01",
                DeviceName = "Offline PC",
                ParentId = pid,
                ChildId = cid,
                LastSeen = DateTime.UtcNow.AddMinutes(-5) // 5 minutes ago
            };
            _db.SaveDeviceStatus(devStale);

            var parentDevices = _db.GetDevicesForParent(pid);
            var onlineDev = parentDevices.Find(d => d.DeviceId == "DEV_ONLINE_01");
            var offlineDev = parentDevices.Find(d => d.DeviceId == "DEV_OFFLINE_01");

            Assert.NotNull(onlineDev);
            Assert.True(onlineDev.IsOnline);

            Assert.NotNull(offlineDev);
            Assert.False(offlineDev.IsOnline);
        }

        [Fact]
        public void Test31_PersistentDeviceId_PreservedOnRestart()
        {
            using var usageTracker1 = new UsageTrackingService(_db);
            using var enforcement1 = new EnforcementService(_db, usageTracker1);
            using var syncService1 = new DeviceSyncService(_db, usageTracker1, enforcement1);

            string devId1 = syncService1.DeviceId;
            Assert.NotEmpty(devId1);

            syncService1.Stop();

            using var usageTracker2 = new UsageTrackingService(_db);
            using var enforcement2 = new EnforcementService(_db, usageTracker2);
            using var syncService2 = new DeviceSyncService(_db, usageTracker2, enforcement2);

            string devId2 = syncService2.DeviceId;
            Assert.Equal(devId1, devId2); // Must preserve persistent device ID
        }

        [Fact]
        public async System.Threading.Tasks.Task Test32_RemoteCommand_Expiration_Validation()
        {
            using var usageTracker = new UsageTrackingService(_db);
            using var enforcement = new EnforcementService(_db, usageTracker);
            using var syncService = new DeviceSyncService(_db, usageTracker, enforcement);

            var expiredCmd = new RemoteCommand
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = "default",
                DeviceId = syncService.DeviceId,
                CommandType = RemoteCommandType.LockDevice,
                Status = CommandStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddHours(-48),
                ExpiresAt = DateTime.UtcNow.AddHours(-24) // Expired 24 hours ago
            };

            await syncService.ExecuteCommandLocallyAsync(expiredCmd);

            var checkedCmd = _db.GetRemoteCommand(expiredCmd.Id);
            Assert.NotNull(checkedCmd);
            Assert.Equal(CommandStatus.Expired, checkedCmd.Status);
        }

        [Fact]
        public async System.Threading.Tasks.Task Test33_All_Ten_RemoteCommands_EndToEnd_Execution()
        {
            using var usageTracker = new UsageTrackingService(_db);
            using var enforcement = new EnforcementService(_db, usageTracker);
            using var syncService = new DeviceSyncService(_db, usageTracker, enforcement);
            string devId = syncService.DeviceId;

            // 1. LockDevice
            var cmdLock = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.LockDevice };
            await syncService.ExecuteCommandLocallyAsync(cmdLock);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdLock.Id)?.Status);

            // 2. UnlockDevice
            var cmdUnlock = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.UnlockDevice };
            await syncService.ExecuteCommandLocallyAsync(cmdUnlock);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdUnlock.Id)?.Status);

            // 3. StartAfk
            var cmdStartAfk = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.StartAfk, PayloadJson = "{\"durationMinutes\":45}" };
            await syncService.ExecuteCommandLocallyAsync(cmdStartAfk);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdStartAfk.Id)?.Status);
            Assert.True(usageTracker.IsAfkModeActive);

            // 4. EndAfk
            var cmdEndAfk = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.EndAfk };
            await syncService.ExecuteCommandLocallyAsync(cmdEndAfk);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdEndAfk.Id)?.Status);
            Assert.False(usageTracker.IsAfkModeActive);

            // 5. BlockApplication
            var cmdBlockApp = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.BlockApplication, PayloadJson = "{\"processName\":\"testgame.exe\",\"displayName\":\"Test Game\"}" };
            await syncService.ExecuteCommandLocallyAsync(cmdBlockApp);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdBlockApp.Id)?.Status);
            var appRules = _db.GetAllAppRules();
            Assert.Contains(appRules, r => r.ProcessName == "testgame.exe" && r.IsBlocked);

            // 6. UnblockApplication
            var cmdUnblockApp = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.UnblockApplication, PayloadJson = "{\"processName\":\"testgame.exe\"}" };
            await syncService.ExecuteCommandLocallyAsync(cmdUnblockApp);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdUnblockApp.Id)?.Status);
            Assert.DoesNotContain(_db.GetAllAppRules(), r => r.ProcessName == "testgame.exe");

            // 7. BlockWebsite
            var cmdBlockWeb = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.BlockWebsite, PayloadJson = "{\"domain\":\"badsite.com\"}" };
            await syncService.ExecuteCommandLocallyAsync(cmdBlockWeb);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdBlockWeb.Id)?.Status);
            Assert.Contains(_db.GetWebRules(), w => w.Domain == "badsite.com" && w.IsBlocked);

            // 8. UnblockWebsite
            var cmdUnblockWeb = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.UnblockWebsite, PayloadJson = "{\"domain\":\"badsite.com\"}" };
            await syncService.ExecuteCommandLocallyAsync(cmdUnblockWeb);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdUnblockWeb.Id)?.Status);
            Assert.DoesNotContain(_db.GetWebRules(), w => w.Domain == "badsite.com");

            // 9. AddTime
            long secondsBefore = _db.GetOrCreateDailyUsage(DateTime.Today, "default").ManualBonusSeconds;
            var cmdAddTime = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.AddTime, PayloadJson = "{\"minutes\":30}" };
            await syncService.ExecuteCommandLocallyAsync(cmdAddTime);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdAddTime.Id)?.Status);
            long secondsAfter = _db.GetOrCreateDailyUsage(DateTime.Today, "default").ManualBonusSeconds;
            Assert.Equal(secondsBefore + 1800, secondsAfter);

            // 10. RefreshPolicy
            var cmdRefresh = new RemoteCommand { Id = Guid.NewGuid().ToString(), DeviceId = devId, CommandType = RemoteCommandType.RefreshPolicy };
            await syncService.ExecuteCommandLocallyAsync(cmdRefresh);
            Assert.Equal(CommandStatus.Executed, _db.GetRemoteCommand(cmdRefresh.Id)?.Status);
        }

        [Fact]
        public void Test35_UserAccountUnique_And_DeviceAccountLinking()
        {
            var authService = new AuthenticationService(_db);

            string user = $"unique_child_{Guid.NewGuid():N}";
            string pass = "StrongPassword123";

            var created = authService.RegisterUser(user, pass, UserRole.Parent);
            Assert.NotNull(created);
            Assert.Equal(user, created.Username);

            // Verifying duplicate username registration throws exception
            Assert.Throws<InvalidOperationException>(() => authService.RegisterUser(user, "AnotherPass"));

            // Verify authentication works
            var verified = authService.VerifyUserCredentials(user, pass);
            Assert.NotNull(verified);
            Assert.Equal(created.Id, verified.Id);

            // Save settings linked web account
            var settings = _db.LoadSettings();
            settings.LinkedWebUsername = user;
            _db.SaveSettings(settings);

            var reloaded = _db.LoadSettings();
            Assert.Equal(user, reloaded.LinkedWebUsername);
        }

        [Fact]
        public void Test36_ParentChildInvitationWorkflow_Accept_Decline_Cancel_And_DeviceVisibility()
        {
            var auth = new AuthenticationService(_db);

            // 1. Create Accounts
            var masterDad = auth.RegisterUser($"MasterDad_{Guid.NewGuid():N}", "Pass123!", UserRole.Parent);
            var kid123 = auth.RegisterUser($"Kid123_{Guid.NewGuid():N}", "Pass123!", UserRole.Child);

            var unrelatedDad = auth.RegisterUser($"UnrelatedDad_{Guid.NewGuid():N}", "Pass123!", UserRole.Parent);
            var kid999 = auth.RegisterUser($"Kid999_{Guid.NewGuid():N}", "Pass123!", UserRole.Child);

            // 2. Register Kid123 Device (owned by Kid123)
            var kidDevice = new DeviceStatusRecord
            {
                DeviceId = $"gdev_kid123_{Guid.NewGuid():N}",
                DeviceName = "Kid123 Gaming Laptop",
                ChildId = kid123.Id,
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            };
            _db.SaveDeviceStatus(kidDevice);

            // 3. Strict Initial Isolation: MasterDad sees NO children and NO devices
            Assert.Empty(_db.GetChildrenForParent(masterDad.Id));
            Assert.Empty(_db.GetDevicesForParent(masterDad.Id));

            // 4. Send Invitation from MasterDad -> Kid123
            var inv = new ParentChildInvitation
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = masterDad.Id,
                ParentUsername = masterDad.Username,
                ChildUserId = kid123.Id,
                ChildUsername = kid123.Username,
                Status = InvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.SaveInvitation(inv);

            // Verify Pending state does NOT grant access
            Assert.False(_db.IsParentConnectedToChild(masterDad.Id, kid123.Id));
            Assert.Empty(_db.GetChildrenForParent(masterDad.Id));
            Assert.Empty(_db.GetDevicesForParent(masterDad.Id));

            // 5. Kid123 Accepts Invitation
            _db.UpdateInvitationStatus(inv.Id, InvitationStatus.Accepted, DateTime.UtcNow);
            _db.SaveRelationship(new ParentChildRelationship
            {
                ParentUserId = masterDad.Id,
                ChildUserId = kid123.Id,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });

            // 6. Verify Active Access Granted
            Assert.True(_db.IsParentConnectedToChild(masterDad.Id, kid123.Id));
            var parentDevices = _db.GetDevicesForParent(masterDad.Id);
            Assert.Single(parentDevices);
            Assert.Equal(kidDevice.DeviceId, parentDevices[0].DeviceId);

            // 7. Verify Unrelated Parent CANNOT see Kid123 or Kid123's device
            Assert.False(_db.IsParentConnectedToChild(unrelatedDad.Id, kid123.Id));
            Assert.Empty(_db.GetChildrenForParent(unrelatedDad.Id));
            Assert.Empty(_db.GetDevicesForParent(unrelatedDad.Id));

            // 8. Test Decline Flow
            var invDecline = new ParentChildInvitation
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = masterDad.Id,
                ParentUsername = masterDad.Username,
                ChildUserId = kid999.Id,
                ChildUsername = kid999.Username,
                Status = InvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _db.SaveInvitation(invDecline);
            _db.UpdateInvitationStatus(invDecline.Id, InvitationStatus.Declined, DateTime.UtcNow);

            var loadedDeclined = _db.GetInvitation(invDecline.Id);
            Assert.NotNull(loadedDeclined);
            Assert.Equal(InvitationStatus.Declined, loadedDeclined.Status);
            Assert.False(_db.IsParentConnectedToChild(masterDad.Id, kid999.Id));

            // 9. Test Cancel Flow
            var invCancel = new ParentChildInvitation
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = masterDad.Id,
                ParentUsername = masterDad.Username,
                ChildUserId = kid999.Id,
                ChildUsername = kid999.Username,
                Status = InvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _db.SaveInvitation(invCancel);
            _db.UpdateInvitationStatus(invCancel.Id, InvitationStatus.Cancelled, DateTime.UtcNow);

            var loadedCancelled = _db.GetInvitation(invCancel.Id);
            Assert.NotNull(loadedCancelled);
            Assert.Equal(InvitationStatus.Cancelled, loadedCancelled.Status);
        }

        [Fact]
        public void Test37_ExactTwoAccountTest_ParentTest_And_ChildTest()
        {
            var auth = new AuthenticationService(_db);

            // 1. ParentTest and ChildTest accounts created
            var parentTest = auth.RegisterUser("ParentTest", "ParentPass123!", UserRole.Parent);
            var childTest = auth.RegisterUser("ChildTest", "ChildPass123!", UserRole.Child);
            var unrelatedParent = auth.RegisterUser("UnrelatedParent", "UnrelatedPass123!", UserRole.Parent);

            // 2. Register ChildTest's Windows device owned by ChildTest
            var childDevice = new DeviceStatusRecord
            {
                DeviceId = "dev_child_test_pc",
                DeviceName = "ChildTest Desktop",
                ChildId = childTest.Id,
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            };
            _db.SaveDeviceStatus(childDevice);

            // Verify initial state: ParentTest does not see ChildTest or ChildTest's device yet
            Assert.Empty(_db.GetChildrenForParent(parentTest.Id));
            Assert.Empty(_db.GetDevicesForParent(parentTest.Id));

            // 3. ParentTest sends request to ChildTest
            var invitation = new ParentChildInvitation
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = parentTest.Id,
                ParentUsername = parentTest.Username,
                ChildUserId = childTest.Id,
                ChildUsername = childTest.Username,
                Status = InvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.SaveInvitation(invitation);

            // 4. ChildTest sees request
            var childPendingInvs = _db.GetPendingInvitationsForChild(childTest.Id);
            Assert.Single(childPendingInvs);
            Assert.Equal(parentTest.Username, childPendingInvs[0].ParentUsername);

            // 5. ChildTest accepts request
            _db.UpdateInvitationStatus(invitation.Id, InvitationStatus.Accepted, DateTime.UtcNow);
            _db.SaveRelationship(new ParentChildRelationship
            {
                ParentUserId = parentTest.Id,
                ChildUserId = childTest.Id,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            _db.SaveChild(new ChildProfile
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = parentTest.Id,
                UserId = childTest.Id,
                Name = childTest.Username,
                IsActive = true
            });

            // 6. ParentTest sees ChildTest
            var childrenOfParent = _db.GetChildrenForParent(parentTest.Id);
            Assert.Single(childrenOfParent);
            Assert.Equal(childTest.Id, childrenOfParent[0].UserId);
            Assert.Equal(childTest.Username, childrenOfParent[0].Name);

            // 7. ChildTest's registered device appears for ParentTest
            var parentDevices = _db.GetDevicesForParent(parentTest.Id);
            Assert.Single(parentDevices);
            Assert.Equal(childDevice.DeviceId, parentDevices[0].DeviceId);

            // 8. ParentTest can access that device
            Assert.True(_db.IsParentConnectedToChild(parentTest.Id, childTest.Id));

            // 9. An unrelated parent cannot see it
            Assert.False(_db.IsParentConnectedToChild(unrelatedParent.Id, childTest.Id));
            Assert.Empty(_db.GetChildrenForParent(unrelatedParent.Id));
            Assert.Empty(_db.GetDevicesForParent(unrelatedParent.Id));
        }
    }
}

