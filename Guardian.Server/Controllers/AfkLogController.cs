using System;
using Microsoft.AspNetCore.Mvc;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AfkLogController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public AfkLogController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet("logs")]
        public IActionResult GetLogs()
        {
            var sessions = _db.GetAllAfkSessions();
            return Ok(sessions);
        }

        [HttpGet("summary")]
        public IActionResult GetSummary()
        {
            var summary = _db.GetAfkSummaryStats();
            return Ok(new
            {
                todayCount = summary.TodayCount,
                todaySeconds = summary.TodaySeconds,
                weekCount = summary.WeekCount,
                weekSeconds = summary.WeekSeconds,
                totalCount = summary.TotalCount,
                totalSeconds = summary.TotalSeconds
            });
        }
    }
}
