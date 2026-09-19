using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface IDeviceSyncService
    {
        bool IsConnected { get; }
        string ServerUrl { get; set; }
        event Action<RemoteCommand>? OnCommandReceived;
        event Action<RemoteCommand>? OnCommandProcessed;

        void Start(string? serverUrl = null);
        void Stop();
        Task ExecuteCommandLocallyAsync(RemoteCommand command);
        Task<bool> DispatchCommandToServerAsync(RemoteCommand command);
    }

    public class DeviceSyncService : IDeviceSyncService, IDisposable
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usageTracker;
        private readonly IEnforcementService _enforcement;
        private readonly HttpClient _httpClient;

        private CancellationTokenSource? _cts;
        private Task? _syncLoopTask;

        public bool IsConnected { get; private set; }
        public string ServerUrl { get; set; } = "https://localhost:7185";

        public event Action<RemoteCommand>? OnCommandReceived;
        public event Action<RemoteCommand>? OnCommandProcessed;

        public DeviceSyncService(IDatabaseService db, IUsageTrackingService usageTracker, IEnforcementService enforcement)
        {
            _db = db;
            _usageTracker = usageTracker;
            _enforcement = enforcement;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        public void Start(string? serverUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                ServerUrl = serverUrl;
            }

            if (_syncLoopTask != null) return;

            _cts = new CancellationTokenSource();
            _syncLoopTask = Task.Run(() => SyncLoopAsync(_cts.Token));
            Logger.Info($"DeviceSyncService started with ServerUrl: {ServerUrl}");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try { _syncLoopTask?.Wait(2000); } catch { }
                _cts.Dispose();
                _cts = null;
                _syncLoopTask = null;
            }
            Logger.Info("DeviceSyncService stopped.");
        }

        private async Task SyncLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. Local device status creation & DB update
                    var status = new DeviceStatusRecord
                    {
                        DeviceId = Environment.MachineName,
                        DeviceName = Environment.MachineName,
                        ChildId = _usageTracker.ActiveChildId,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow,
                        IsGuardianRunning = true,
                        IsProtected = _enforcement.IsEnforcementActive,
                        ScreenTimeUsedSeconds = _usageTracker.TotalDeviceSecondsToday,
                        CurrentApp = string.IsNullOrEmpty(_usageTracker.ActiveProcessName) ? "Desktop / Idle" : _usageTracker.ActiveProcessName,
                        AfkActive = _usageTracker.IsAfkModeActive
                    };

                    _db.SaveDeviceStatus(status);

                    // 2. Transmit heartbeat to server if available
                    await SendHeartbeatAsync(status, token);

                    // 3. Fetch & execute pending remote commands
                    await ProcessPendingCommandsAsync(token);
                }
                catch (Exception ex)
                {
                    Logger.Error("Error in DeviceSyncService loop", ex);
                    IsConnected = false;
                }

                await Task.Delay(5000, token);
            }
        }

        private async Task SendHeartbeatAsync(DeviceStatusRecord status, CancellationToken token)
        {
            try
            {
                string url = $"{ServerUrl.TrimEnd('/')}/api/devices/heartbeat";
                var response = await _httpClient.PostAsJsonAsync(url, status, token);
                IsConnected = response.IsSuccessStatusCode;
            }
            catch
            {
                IsConnected = false;
            }
        }

        private async Task ProcessPendingCommandsAsync(CancellationToken token)
        {
            // A. Check local DB pending commands
            var localPending = _db.GetPendingCommandsForDevice(Environment.MachineName);

            // B. Check remote server pending commands if connected
            if (IsConnected)
            {
                try
                {
                    string url = $"{ServerUrl.TrimEnd('/')}/api/devices/commands/pending?deviceId={Uri.EscapeDataString(Environment.MachineName)}";
                    var remoteCommands = await _httpClient.GetFromJsonAsync<List<RemoteCommand>>(url, cancellationToken: token);
                    if (remoteCommands != null)
                    {
                        foreach (var cmd in remoteCommands)
                        {
                            if (!_db.GetRemoteCommand(cmd.Id)?.Status.Equals(CommandStatus.Executed) ?? true)
                            {
                                _db.SaveRemoteCommand(cmd);
                                if (!localPending.Exists(c => c.Id == cmd.Id))
                                {
                                    localPending.Add(cmd);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to fetch remote commands from server", ex);
                }
            }

            // C. Execute each command
            foreach (var cmd in localPending)
            {
                if (token.IsCancellationRequested) break;
                await ExecuteCommandLocallyAsync(cmd);
            }
        }

        public async Task ExecuteCommandLocallyAsync(RemoteCommand command)
        {
            try
            {
                Logger.Info($"Processing RemoteCommand [{command.CommandType}] ID: {command.Id}");
                OnCommandReceived?.Invoke(command);

                _db.SaveRemoteCommand(command);
                _db.UpdateCommandStatus(command.Id, CommandStatus.Executing);
                NotifyCommandStatusToServer(command.Id, CommandStatus.Executing);

                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(command.PayloadJson) ? "{}" : command.PayloadJson);
                var root = doc.RootElement;

                switch (command.CommandType)
                {
                    case RemoteCommandType.LockDevice:
                        _usageTracker.LockDeviceManually("Device remotely locked by Parent Dashboard");
                        break;

                    case RemoteCommandType.BlockApp:
                        {
                            string? procName = GetJsonStringProp(root, "processName", "ProcessName", "process_name");
                            string? dispName = GetJsonStringProp(root, "displayName", "DisplayName", "display_name") ?? procName;
                            if (!string.IsNullOrWhiteSpace(procName))
                            {
                                _db.SaveAppRule(new AppRule
                                {
                                    ProcessName = procName.ToLowerInvariant(),
                                    DisplayName = dispName ?? procName,
                                    IsBlocked = true,
                                    Category = "Blocked Remotely"
                                });
                                _enforcement.RefreshRules();
                            }
                        }
                        break;

                    case RemoteCommandType.UnblockApp:
                        {
                            string? procName = GetJsonStringProp(root, "processName", "ProcessName", "process_name");
                            if (!string.IsNullOrWhiteSpace(procName))
                            {
                                _db.DeleteAppRule(procName);
                                _enforcement.RefreshRules();
                            }
                        }
                        break;

                    case RemoteCommandType.BlockWebsite:
                        {
                            string? domain = GetJsonStringProp(root, "domain", "Domain");
                            if (!string.IsNullOrWhiteSpace(domain))
                            {
                                _db.SaveWebRule(new WebFilterRule
                                {
                                    Domain = domain,
                                    IsBlocked = true,
                                    Category = "Parent Blocked",
                                    Reason = "Blocked from Remote Dashboard"
                                });
                            }
                        }
                        break;

                    case RemoteCommandType.UnblockWebsite:
                        {
                            string? domain = GetJsonStringProp(root, "domain", "Domain");
                            if (!string.IsNullOrWhiteSpace(domain))
                            {
                                _db.DeleteWebRule(domain);
                            }
                        }
                        break;

                    case RemoteCommandType.SetDailyLimit:
                        if (root.TryGetProperty("dailyLimitMinutes", out var limitProp) && limitProp.TryGetInt32(out int limitMins))
                        {
                            var child = _db.GetChild(command.ChildId) ?? new ChildProfile { Id = command.ChildId, Name = "Alex" };
                            child.DailyLimitMinutes = limitMins;
                            _db.SaveChild(child);
                            _usageTracker.RefreshSchedule();
                        }
                        break;

                    case RemoteCommandType.GrantBonusTime:
                        if (root.TryGetProperty("minutes", out var minProp) && minProp.TryGetInt32(out int bonusMins))
                        {
                            _usageTracker.GrantBonusMinutes(bonusMins);
                        }
                        break;

                    case RemoteCommandType.RequestDecision:
                        if (root.TryGetProperty("requestId", out var reqIdProp))
                        {
                            string reqId = reqIdProp.GetString() ?? "";
                            bool approved = root.TryGetProperty("approved", out var appProp) && appProp.GetBoolean();
                            int addMins = root.TryGetProperty("minutes", out var addMinsProp) && addMinsProp.TryGetInt32(out int m) ? m : 30;

                            var requests = _db.GetRequests();
                            var targetReq = requests.Find(r => r.Id == reqId);
                            if (targetReq != null)
                            {
                                targetReq.Status = approved ? RequestStatus.Approved : RequestStatus.Denied;
                                targetReq.RespondedAt = DateTime.Now;
                                _db.SaveRequest(targetReq);

                                if (approved)
                                {
                                    _usageTracker.GrantBonusMinutes(addMins);
                                }
                            }
                        }
                        break;

                    case RemoteCommandType.ActivateAfk:
                        int duration = root.TryGetProperty("durationMinutes", out var durProp) && durProp.TryGetInt32(out int d) ? d : 60;
                        _db.SaveAfkState(new AfkState
                        {
                            IsAfkModeActive = true,
                            AfkStartedAt = DateTime.Now,
                            AfkDurationMinutes = duration,
                            ActivatedBy = "Parent Remote"
                        });
                        _db.SaveAfkSession(new AfkSessionRecord
                        {
                            StartTimestamp = DateTime.Now,
                            ActivationMethod = "Remote Parent Website",
                            ActivatedBy = "Parent Remote",
                            EndReason = "Active"
                        });
                        break;

                    case RemoteCommandType.DeactivateAfk:
                        _usageTracker.ForceDeactivateAfkMode("Parent Remote Disable");
                        break;
                }

                command.Status = CommandStatus.Executed;
                command.ExecutedAt = DateTime.UtcNow;
                _db.UpdateCommandStatus(command.Id, CommandStatus.Executed);
                NotifyCommandStatusToServer(command.Id, CommandStatus.Executed);
                OnCommandProcessed?.Invoke(command);
                Logger.Info($"RemoteCommand [{command.CommandType}] ID: {command.Id} EXECUTED successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute RemoteCommand ID: {command.Id}", ex);
                command.Status = CommandStatus.Failed;
                command.ErrorMessage = ex.Message;
                _db.UpdateCommandStatus(command.Id, CommandStatus.Failed, ex.Message);
                NotifyCommandStatusToServer(command.Id, CommandStatus.Failed, ex.Message);
            }
        }

        public async Task<bool> DispatchCommandToServerAsync(RemoteCommand command)
        {
            _db.SaveRemoteCommand(command);

            if (!IsConnected) return false;

            try
            {
                string url = $"{ServerUrl.TrimEnd('/')}/api/commands/create";
                var res = await _httpClient.PostAsJsonAsync(url, command);
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error dispatching command {command.Id} to server", ex);
                return false;
            }
        }

        private async void NotifyCommandStatusToServer(string commandId, CommandStatus status, string? error = null)
        {
            if (!IsConnected) return;

            try
            {
                string url = $"{ServerUrl.TrimEnd('/')}/api/devices/commands/status";
                var payload = new { commandId, status = status.ToString(), errorMessage = error };
                await _httpClient.PostAsJsonAsync(url, payload);
            }
            catch { }
        }

        private static string? GetJsonStringProp(JsonElement root, params string[] names)
        {
            foreach (var n in names)
            {
                if (root.TryGetProperty(n, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    return val.GetString();
                }
            }
            foreach (var prop in root.EnumerateObject())
            {
                foreach (var n in names)
                {
                    if (prop.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        return prop.Value.GetString();
                    }
                }
            }
            return null;
        }

        public void Dispose()
        {
            Stop();
            _httpClient.Dispose();
        }
    }
}
