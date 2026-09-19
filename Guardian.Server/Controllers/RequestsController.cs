using System;
using Microsoft.AspNetCore.Mvc;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    public class DecisionDto
    {
        public bool Approved { get; set; } = true;
        public int Minutes { get; set; } = 30;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RequestsController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usageTracker;

        public RequestsController(IDatabaseService db, IUsageTrackingService usageTracker)
        {
            _db = db;
            _usageTracker = usageTracker;
        }

        [HttpGet]
        public IActionResult GetRequests()
        {
            var requests = _db.GetRequests();
            return Ok(requests);
        }

        [HttpPost("{id}/respond")]
        public IActionResult RespondToRequest(string id, [FromBody] DecisionDto dto)
        {
            var requests = _db.GetRequests();
            var target = requests.Find(r => r.Id == id);
            if (target == null)
            {
                return NotFound(new { success = false, message = "Request not found." });
            }

            target.Status = dto.Approved ? RequestStatus.Approved : RequestStatus.Denied;
            target.RespondedAt = DateTime.Now;
            _db.SaveRequest(target);

            if (dto.Approved)
            {
                _usageTracker.GrantBonusMinutes(dto.Minutes);
                _db.LogIncident("REQUEST_APPROVED", target.TargetProcessName, $"Parent approved {dto.Minutes} bonus minutes for request {id}.");
            }
            else
            {
                _db.LogIncident("REQUEST_DENIED", target.TargetProcessName, $"Parent denied time request {id}.");
            }

            return Ok(new
            {
                success = true,
                message = dto.Approved ? $"Request approved! Added {dto.Minutes} minutes." : "Request denied.",
                request = target
            });
        }
    }
}
