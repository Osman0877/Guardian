using System;
using System.Collections.Generic;
using System.Security.Claims;
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
            string parentId = GetAuthenticatedParentId();
            if (string.IsNullOrEmpty(parentId))
            {
                var allChildren = _db.GetAllChildren();
                return Ok(allChildren);
            }

            var children = _db.GetChildrenForParent(parentId);
            return Ok(children);
        }

        [HttpGet("devices")]
        public IActionResult GetDevices()
        {
            string parentId = GetAuthenticatedParentId();
            if (string.IsNullOrEmpty(parentId))
            {
                var allDevices = _db.GetAllDeviceStatuses();
                return Ok(allDevices);
            }

            var devices = _db.GetDevicesForParent(parentId);
            return Ok(devices);
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
        public IActionResult GetUsage([FromQuery] string? childId)
        {
            if (string.IsNullOrWhiteSpace(childId))
            {
                string parentId = GetAuthenticatedParentId();
                var children = _db.GetChildrenForParent(parentId);
                if (children.Count > 0) childId = children[0].Id;
            }

            if (string.IsNullOrWhiteSpace(childId))
            {
                return Ok(new DailyUsageRecord { DateString = DateTime.Today.ToString("yyyy-MM-dd"), ChildId = "" });
            }

            var usage = _db.GetOrCreateDailyUsage(DateTime.Today, childId);
            return Ok(usage);
        }

        private string GetAuthenticatedParentId()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var parentClaim = User.FindFirst("ParentId")?.Value;
                if (!string.IsNullOrEmpty(parentClaim)) return parentClaim;

                var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(idClaim)) return idClaim;
            }
            return string.Empty;
        }
    }
}
