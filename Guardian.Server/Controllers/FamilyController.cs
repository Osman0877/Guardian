using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FamilyController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public FamilyController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet("children")]
        public IActionResult GetChildren()
        {
            var children = _db.GetAllChildren();
            if (children.Count == 0)
            {
                // Return default Alex child if DB is clean
                children.Add(new ChildProfile
                {
                    Id = "default",
                    Name = "Alex",
                    DailyLimitMinutes = 120,
                    IsActive = true
                });
            }
            return Ok(children);
        }

        [HttpGet("devices")]
        public IActionResult GetDevices()
        {
            var statuses = _db.GetAllDeviceStatuses();
            if (statuses.Count == 0)
            {
                // Populate default device status
                var defaultDevice = new DeviceStatusRecord
                {
                    DeviceId = Environment.MachineName,
                    DeviceName = $"{Environment.MachineName} (Gaming PC)",
                    ChildId = "default",
                    IsOnline = true,
                    LastSeen = DateTime.UtcNow,
                    IsGuardianRunning = true,
                    IsProtected = true,
                    ScreenTimeUsedSeconds = 5040, // 1h 24m
                    CurrentApp = "Roblox",
                    AfkActive = false
                };
                _db.SaveDeviceStatus(defaultDevice);
                statuses.Add(defaultDevice);
            }
            return Ok(statuses);
        }

        [HttpGet("app-rules")]
        public IActionResult GetAppRules()
        {
            var rules = _db.GetAllAppRules();
            return Ok(rules);
        }

        [HttpGet("web-rules")]
        public IActionResult GetWebRules()
        {
            var rules = _db.GetWebRules();
            return Ok(rules);
        }

        [HttpGet("usage")]
        public IActionResult GetUsage([FromQuery] string? childId = "default")
        {
            var usage = _db.GetOrCreateDailyUsage(DateTime.Today, childId ?? "default");
            return Ok(usage);
        }
    }
}
