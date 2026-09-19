using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Guardian.Server.Hubs;

namespace Guardian.Server.Controllers
{
    public class CreateCommandDto
    {
        public string ChildId { get; set; } = "default";
        public string DeviceId { get; set; } = string.Empty;
        public string CommandType { get; set; } = "LockDevice";
        public string PayloadJson { get; set; } = "{}";
    }

    public class CommandStatusUpdateDto
    {
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = "Executed";
        public string? ErrorMessage { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly IHubContext<GuardianHub> _hubContext;

        public CommandsController(IDatabaseService db, IHubContext<GuardianHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandDto dto)
        {
            if (!Enum.TryParse<RemoteCommandType>(dto.CommandType, true, out var type))
            {
                return BadRequest(new { success = false, message = $"Invalid command type: {dto.CommandType}" });
            }

            if (string.IsNullOrWhiteSpace(dto.DeviceId))
            {
                return BadRequest(new { success = false, message = "Target DeviceId is required." });
            }

            var cmd = new RemoteCommand
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = dto.ChildId,
                DeviceId = dto.DeviceId,
                CommandType = type,
                PayloadJson = dto.PayloadJson ?? "{}",
                Status = CommandStatus.Pending,
                CreatedBy = "Parent Web Dashboard",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _db.SaveRemoteCommand(cmd);

            // Direct device group routing ONLY (No global broadcasting)
            await _hubContext.Clients.Group($"device_{cmd.DeviceId}").SendAsync("ReceiveRemoteCommand", cmd);

            _db.LogIncident("REMOTE_COMMAND_SENT", cmd.CommandType.ToString(), $"Remote command [{cmd.CommandType}] queued specifically for device {cmd.DeviceId}.");

            return Ok(new
            {
                success = true,
                commandId = cmd.Id,
                status = cmd.Status.ToString(),
                message = $"Remote command {cmd.CommandType} successfully queued for device {cmd.DeviceId}."
            });
        }

        [HttpGet("recent")]
        public IActionResult GetRecentCommands([FromQuery] int limit = 50)
        {
            var list = _db.GetRecentCommands(limit);
            return Ok(list);
        }

        [HttpGet("/api/devices/commands/pending")]
        public IActionResult GetPendingCommands([FromQuery] string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return BadRequest(new { success = false, message = "deviceId is required." });
            }
            var list = _db.GetPendingCommandsForDevice(deviceId);
            return Ok(list);
        }

        [HttpPost("/api/devices/commands/status")]
        public async Task<IActionResult> UpdateCommandStatus([FromBody] CommandStatusUpdateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CommandId))
            {
                return BadRequest(new { success = false, message = "CommandId is required." });
            }

            if (!Enum.TryParse<CommandStatus>(dto.Status, true, out var status))
            {
                status = CommandStatus.Executed;
            }

            _db.UpdateCommandStatus(dto.CommandId, status, dto.ErrorMessage);

            // Notify parent dashboard WebSockets
            await _hubContext.Clients.Group("all_parents").SendAsync("CommandStatusChanged", new
            {
                commandId = dto.CommandId,
                status = status.ToString(),
                errorMessage = dto.ErrorMessage
            });

            return Ok(new { success = true });
        }

        [HttpPost("/api/devices/heartbeat")]
        public async Task<IActionResult> DeviceHeartbeat([FromBody] DeviceStatusRecord status)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.DeviceId))
            {
                return BadRequest(new { success = false, message = "Invalid device status." });
            }

            status.IsOnline = true;
            status.LastSeen = DateTime.UtcNow;

            _db.SaveDeviceStatus(status);

            // Broadcast real-time status update to parent dashboard
            await _hubContext.Clients.Group("all_parents").SendAsync("DeviceStatusUpdated", status);

            return Ok(new { success = true });
        }
    }
}
