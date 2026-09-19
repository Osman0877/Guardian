using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    public class SendInvitationDto
    {
        public string ChildUsername { get; set; } = string.Empty;
    }

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
            string parentId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(parentId))
            {
                return Ok(new List<ChildProfile>());
            }

            var children = _db.GetChildrenForParent(parentId);
            return Ok(children);
        }

        [HttpGet("devices")]
        public IActionResult GetDevices()
        {
            string parentId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(parentId))
            {
                return Ok(new List<DeviceStatusRecord>());
            }

            var devices = _db.GetDevicesForParent(parentId);
            return Ok(devices);
        }

        [HttpPost("invitations")]
        public IActionResult SendInvitation([FromBody] SendInvitationDto dto)
        {
            string parentId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(parentId))
            {
                return Unauthorized(new { success = false, message = "Authentication required." });
            }

            if (string.IsNullOrWhiteSpace(dto.ChildUsername))
            {
                return BadRequest(new { success = false, message = "Child's Guardian username is required." });
            }

            string cleanChildUsername = dto.ChildUsername.Trim();
            var childUser = _db.GetUserByUsername(cleanChildUsername);

            if (childUser == null)
            {
                return NotFound(new { success = false, message = $"Child account '{cleanChildUsername}' not found. Verify the child's exact Guardian username." });
            }

            if (childUser.Id == parentId)
            {
                return BadRequest(new { success = false, message = "You cannot send a connection request to your own parent account." });
            }

            // Verify if active relationship already exists
            if (_db.IsParentConnectedToChild(parentId, childUser.Id))
            {
                return BadRequest(new { success = false, message = $"You are already connected to child '{childUser.Username}'." });
            }

            // Verify if invitation is already pending
            var existingInvitations = _db.GetInvitationsForParent(parentId);
            if (existingInvitations.Exists(i => i.ChildUserId == childUser.Id && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow))
            {
                return BadRequest(new { success = false, message = $"A connection request is already pending for '{childUser.Username}'." });
            }

            var parentUser = _db.GetUserById(parentId);
            string parentUsername = parentUser?.Username ?? User.Identity?.Name ?? "Parent";

            var inv = new ParentChildInvitation
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = parentId,
                ParentUsername = parentUsername,
                ChildUserId = childUser.Id,
                ChildUsername = childUser.Username,
                Status = InvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _db.SaveInvitation(inv);
            _db.LogIncident("INVITATION_SENT", parentUsername, $"Sent connection request to child '{childUser.Username}'.");

            return Ok(new
            {
                success = true,
                invitation = inv,
                message = $"Connection request sent to '{childUser.Username}'. Status: Pending until accepted by child."
            });
        }

        [HttpGet("invitations")]
        public IActionResult GetInvitations([FromQuery] string? childUserId)
        {
            string userId = GetAuthenticatedUserId();

            if (!string.IsNullOrEmpty(childUserId))
            {
                var pending = _db.GetPendingInvitationsForChild(childUserId);
                return Ok(pending);
            }

            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new List<ParentChildInvitation>());
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Parent";
            if (role == "Child")
            {
                var pending = _db.GetPendingInvitationsForChild(userId);
                return Ok(pending);
            }

            var parentInvs = _db.GetInvitationsForParent(userId);
            return Ok(parentInvs);
        }

        [HttpPost("invitations/{id}/accept")]
        public IActionResult AcceptInvitation(string id, [FromQuery] string? childUserId)
        {
            string authenticatedUserId = GetAuthenticatedUserId();
            string effectiveChildUserId = !string.IsNullOrEmpty(authenticatedUserId) ? authenticatedUserId : (childUserId ?? "");

            var inv = _db.GetInvitation(id);
            if (inv == null)
            {
                return NotFound(new { success = false, message = "Invitation not found." });
            }

            // Security Authorization Check: Verify target child identity
            if (!string.IsNullOrEmpty(effectiveChildUserId) && inv.ChildUserId != effectiveChildUserId)
            {
                var childUser = _db.GetUserById(effectiveChildUserId);
                if (childUser == null || !childUser.Username.Equals(inv.ChildUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(403, new { success = false, message = "You are not authorized to accept this invitation." });
                }
            }

            if (inv.Status != InvitationStatus.Pending || DateTime.UtcNow > inv.ExpiresAt)
            {
                return BadRequest(new { success = false, message = "Invitation is no longer valid or has expired." });
            }

            _db.UpdateInvitationStatus(inv.Id, InvitationStatus.Accepted, DateTime.UtcNow);

            var rel = new ParentChildRelationship
            {
                Id = Guid.NewGuid().ToString(),
                ParentUserId = inv.ParentUserId,
                ChildUserId = inv.ChildUserId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _db.SaveRelationship(rel);

            // Create/link child profile record if missing
            var existingChildren = _db.GetAllChildren();
            var profile = existingChildren.Find(c => c.UserId == inv.ChildUserId || c.Name.Equals(inv.ChildUsername, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                profile = new ChildProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = inv.ParentUserId,
                    UserId = inv.ChildUserId,
                    Name = inv.ChildUsername,
                    IsActive = true
                };
            }
            else
            {
                profile.ParentId = inv.ParentUserId;
                profile.UserId = inv.ChildUserId;
            }
            _db.SaveChild(profile);

            _db.LogIncident("INVITATION_ACCEPTED", inv.ChildUsername, $"Child '{inv.ChildUsername}' accepted connection request from parent '{inv.ParentUsername}'.");

            return Ok(new
            {
                success = true,
                message = $"Successfully connected to parent '{inv.ParentUsername}'!"
            });
        }

        [HttpPost("invitations/{id}/decline")]
        public IActionResult DeclineInvitation(string id, [FromQuery] string? childUserId)
        {
            string authenticatedUserId = GetAuthenticatedUserId();
            string effectiveChildUserId = !string.IsNullOrEmpty(authenticatedUserId) ? authenticatedUserId : (childUserId ?? "");

            var inv = _db.GetInvitation(id);
            if (inv == null)
            {
                return NotFound(new { success = false, message = "Invitation not found." });
            }

            if (!string.IsNullOrEmpty(effectiveChildUserId) && inv.ChildUserId != effectiveChildUserId)
            {
                var childUser = _db.GetUserById(effectiveChildUserId);
                if (childUser == null || !childUser.Username.Equals(inv.ChildUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(403, new { success = false, message = "You are not authorized to decline this invitation." });
                }
            }

            _db.UpdateInvitationStatus(inv.Id, InvitationStatus.Declined, DateTime.UtcNow);
            _db.LogIncident("INVITATION_DECLINED", inv.ChildUsername, $"Child '{inv.ChildUsername}' declined connection request from parent '{inv.ParentUsername}'.");

            return Ok(new
            {
                success = true,
                message = "Connection request declined."
            });
        }

        [HttpPost("invitations/{id}/cancel")]
        public IActionResult CancelInvitation(string id)
        {
            string parentId = GetAuthenticatedUserId();

            var inv = _db.GetInvitation(id);
            if (inv == null)
            {
                return NotFound(new { success = false, message = "Invitation not found." });
            }

            if (!string.IsNullOrEmpty(parentId) && inv.ParentUserId != parentId)
            {
                return StatusCode(403, new { success = false, message = "You are not authorized to cancel this invitation." });
            }

            _db.UpdateInvitationStatus(inv.Id, InvitationStatus.Cancelled, DateTime.UtcNow);
            return Ok(new { success = true, message = "Connection request cancelled." });
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
                string parentId = GetAuthenticatedUserId();
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

        private string GetAuthenticatedUserId()
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
