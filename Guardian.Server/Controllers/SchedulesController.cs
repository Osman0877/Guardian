using System;
using System.Collections.Generic;
using System.Security.Claims;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Guardian.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulesController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public SchedulesController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetSchedules()
        {
            var list = _db.GetAllAdvancedSchedules();
            if (list.Count == 0)
            {
                // Return default schedules for all 7 days if none present
                for (int i = 0; i < 7; i++)
                {
                    list.Add(new AdvancedDaySchedule
                    {
                        Day = (DayOfWeek)i,
                        LimitMinutes = 120,
                        HasAllowedHours = false,
                        AllowedStartTime = new TimeSpan(17, 0, 0),
                        AllowedEndTime = new TimeSpan(20, 0, 0),
                        BedtimeEnabled = false,
                        Bedtime = new TimeSpan(21, 30, 0)
                    });
                }
            }

            return Ok(list);
        }

        [HttpPost]
        public IActionResult SaveSchedules([FromBody] List<AdvancedDayScheduleDto> request)
        {
            if (request == null || request.Count == 0)
            {
                return BadRequest(new { success = false, message = "No schedules provided." });
            }

            foreach (var item in request)
            {
                var sched = new AdvancedDaySchedule
                {
                    Day = item.Day,
                    LimitMinutes = Math.Max(0, item.LimitMinutes),
                    HasAllowedHours = item.HasAllowedHours,
                    AllowedStartTime = TimeSpan.TryParse(item.AllowedStartTime, out var st) ? st : new TimeSpan(17, 0, 0),
                    AllowedEndTime = TimeSpan.TryParse(item.AllowedEndTime, out var et) ? et : new TimeSpan(20, 0, 0),
                    BedtimeEnabled = item.BedtimeEnabled,
                    Bedtime = TimeSpan.TryParse(item.Bedtime, out var bt) ? bt : new TimeSpan(21, 30, 0)
                };

                _db.SaveAdvancedSchedule(sched);
            }

            // Queue a RefreshPolicy command to all active devices connected to this parent
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                if (!string.IsNullOrEmpty(userId))
                {
                    var parentDevices = _db.GetDevicesForParent(userId);
                    foreach (var dev in parentDevices)
                    {
                        var cmd = new RemoteCommand
                        {
                            Id = Guid.NewGuid().ToString(),
                            ChildId = dev.ChildId,
                            DeviceId = dev.DeviceId,
                            CommandType = RemoteCommandType.RefreshPolicy,
                            PayloadJson = "{}",
                            Status = CommandStatus.Pending,
                            CreatedBy = User.Identity?.Name ?? "Parent",
                            CreatedAt = DateTime.UtcNow,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                        };
                        _db.SaveRemoteCommand(cmd);
                    }
                }
            }
            catch { }

            _db.LogIncident("SCHEDULES_UPDATED", User.Identity?.Name ?? "Parent", "Parent updated screen-time, bedtime, and school-time schedules.");

            return Ok(new
            {
                success = true,
                message = "Schedules updated successfully and policy refresh queued."
            });
        }
    }

    public class AdvancedDayScheduleDto
    {
        public DayOfWeek Day { get; set; }
        public int LimitMinutes { get; set; }
        public bool HasAllowedHours { get; set; }
        public string AllowedStartTime { get; set; } = "17:00:00";
        public string AllowedEndTime { get; set; } = "20:00:00";
        public bool BedtimeEnabled { get; set; }
        public string Bedtime { get; set; } = "21:30:00";
    }
}
