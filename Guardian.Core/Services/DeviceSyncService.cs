using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Guardian.Core.Models;
using Guardian.Core.Win32;

namespace Guardian.Core.Services
{
    public interface IDeviceSyncService
    {
        bool IsConnected { get; }
        string ServerUrl { get; set; }
        string DeviceId { get; }
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
        private HubConnection? _hubConnection;
        private string _deviceId = string.Empty;

        public bool IsConnected { get; private set; }
        public string ServerUrl { get; set; } = "https://guardian-parent-server.onrender.com";
        public string DeviceId => _deviceId;

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

            _deviceId = GetOrCreatePersistentDeviceId();
        }

        private string GetOrCreatePersistentDeviceId()
        {
            var settings = _db.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.DeviceId))
            {
                settings.DeviceId = $"gdev_{Guid.NewGuid().ToString("N").Substring(0, 12)}";
                _db.SaveSettings(settings);
            }
            return settings.DeviceId;
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
            InitSignalRConnectionAsync().Forget();

            Logger.Info($"DeviceSyncService started with Persistent Device ID: {_deviceId}, ServerUrl: {ServerUrl}");
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

            if (_hubConnection != null)
            {
                try { _hubConnection.StopAsync().Wait(1000); } catch { }
                _hubConnection.DisposeAsync().AsTask().Wait(1000);
                _hubConnection = null;
            }

            Logger.Info("DeviceSyncService stopped.");
        }

        private async Task InitSignalRConnectionAsync()
        {
            try
            {
                string hubUrl = $"{ServerUrl.TrimEnd('/')}/hubs/guardian";
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<RemoteCommand>("ReceiveRemoteCommand", async (command) =>
                {
                    Logger.Info($"SignalR Push received RemoteCommand [{command.CommandType}] ID: {command.Id}");
                    await ExecuteCommandLocallyAsync(command);
                });

                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("RegisterDevice", _deviceId);
                IsConnected = true;
                Logger.Info($"SignalR connected and registered device: {_deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"SignalR connection deferred: {ex.Message}");
            }
        }

        private async Task SyncLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string currentApp = NativeMethods.GetActiveProcessName();
                    if (string.IsNullOrEmpty(currentApp))
                    {
                        currentApp = string.IsNullOrEmpty(_usageTracker.ActiveProcessName) ? "Desktop / Idle" : _usageTracker.ActiveProcessName;
                    }

                    var status = new DeviceStatusRecord
                    {
                        DeviceId = _deviceId,
                        DeviceName = Environment.MachineName,
                        ChildId = _usageTracker.ActiveChildId,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow,
                        IsGuardianRunning = true,
                        IsProtected = _enforcement.IsEnforcementActive,
                        ScreenTimeUsedSeconds = _usageTracker.TotalDeviceSecondsToday,
                        CurrentApp = currentApp,
                        AfkActive = _usageTracker.IsAfkModeActive
                    };

                    _db.SaveDeviceStatus(status);

                    await SendHeartbeatAsync(status, token);
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
            var localPending = _db.GetPendingCommandsForDevice(_deviceId);

            if (IsConnected)
            {
                try
                {
                    string url = $"{ServerUrl.TrimEnd('/')}/api/devices/commands/pending?deviceId={Uri.EscapeDataString(_deviceId)}";
                    var remoteCommands = await _httpClient.GetFromJsonAsync<List<RemoteCommand>>(url, cancellationToken: token);
                    if (remoteCommands != null)
                    {
                        foreach (var cmd in remoteCommands)
                        {
                            var existing = _db.GetRemoteCommand(cmd.Id);
                            if (existing == null || existing.Status == CommandStatus.Pending || existing.Status == CommandStatus.Delivered)
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
                if (command.ExpiresAt != default && DateTime.UtcNow > command.ExpiresAt)
                {
                    Logger.Warn($"RemoteCommand ID {command.Id} has EXPIRED ({command.ExpiresAt}). Skipping execution.");
                    command.Status = CommandStatus.Expired;
                    command.ErrorMessage = "Command expired before execution.";
                    _db.SaveRemoteCommand(command);
                    _db.UpdateCommandStatus(command.Id, CommandStatus.Expired, command.ErrorMessage);
                    NotifyCommandStatusToServer(command.Id, CommandStatus.Expired, command.ErrorMessage);
                    return;
                }

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
                        try { NativeMethods.LockWorkStation(); } catch { }
                        break;

                    case RemoteCommandType.UnlockDevice:
                        _usageTracker.UnlockDeviceManually("Device remotely unlocked by Parent Dashboard");
                        break;

                    case RemoteCommandType.BlockApp:
                    case RemoteCommandType.BlockApplication:
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
                    case RemoteCommandType.UnblockApplication:
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
                            var child = _db.GetChild(command.ChildId) ?? new ChildProfile { Id = command.ChildId, Name = "Child" };
                            child.DailyLimitMinutes = limitMins;
                            _db.SaveChild(child);
                            _usageTracker.RefreshSchedule();
                        }
                        break;

                    case RemoteCommandType.GrantBonusTime:
                    case RemoteCommandType.AddTime:
                        if ((root.TryGetProperty("minutes", out var minProp) || root.TryGetProperty("RequestedMinutes", out minProp)) && minProp.TryGetInt32(out int bonusMins))
                        {
                            _usageTracker.GrantBonusMinutes(bonusMins);
                        }
                        break;

                    case RemoteCommandType.ActivateAfk:
                    case RemoteCommandType.StartAfk:
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
                            StartTimestamp = DateTime.UtcNow,
                            ActivationMethod = "Remote Parent Website",
                            ActivatedBy = "Parent Remote",
                            EndReason = "Active"
                        });
                        break;

                    case RemoteCommandType.DeactivateAfk:
                    case RemoteCommandType.EndAfk:
                        _usageTracker.ForceDeactivateAfkMode("Parent Remote Disable");
                        break;

                    case RemoteCommandType.RefreshPolicy:
                        _enforcement.RefreshRules();
                        _usageTracker.RefreshSchedule();
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

    internal static class TaskExtensions
    {
        public static void Forget(this Task task)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Logger.Error("Background task faulted", t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
