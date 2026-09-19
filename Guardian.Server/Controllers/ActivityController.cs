using System;
using Microsoft.AspNetCore.Mvc;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivityController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public ActivityController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetActivity([FromQuery] int limit = 50)
        {
            var incidents = _db.GetIncidents(limit);
            var list = new System.Collections.Generic.List<object>();
            foreach (var inc in incidents)
            {
                list.Add(new
                {
                    timestamp = inc.Timestamp,
                    eventType = inc.EventType,
                    processName = inc.ProcessName,
                    description = inc.Description
                });
            }
            return Ok(list);
        }
    }
}
