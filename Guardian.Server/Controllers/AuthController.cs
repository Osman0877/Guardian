using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace Guardian.Server.Controllers
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly IDatabaseService _db;
        private static readonly ConcurrentDictionary<string, (int Count, DateTime LockoutUntil)> _loginAttempts = new();
        public static string JwtSecret => Environment.GetEnvironmentVariable("JWT_SECRET") ?? "GuardianSecretKey_MustBeAtLeast32BytesLongForSecurity_2026!";

        public AuthController(IAuthenticationService authService, IDatabaseService db)
        {
            _authService = authService;
            _db = db;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponse { Success = false, Message = "Username and password are required." });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string rateKey = $"{clientIp}_{request.Username.ToLowerInvariant()}";

            // Rate limiting check: max 5 failed attempts per 5 minutes
            if (_loginAttempts.TryGetValue(rateKey, out var attempt))
            {
                if (DateTime.UtcNow < attempt.LockoutUntil)
                {
                    TimeSpan remaining = attempt.LockoutUntil - DateTime.UtcNow;
                    return StatusCode(429, new LoginResponse 
                    { 
                        Success = false, 
                        Message = $"Too many failed login attempts. Please wait {Math.Ceiling(remaining.TotalSeconds)} seconds." 
                    });
                }
            }

            // Reject child logins explicitly if username matches a known child profile or is not a parent
            var children = _db.GetAllChildren();
            if (children.Exists(c => c.Name.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
            {
                return Unauthorized(new LoginResponse 
                { 
                    Success = false, 
                    Message = "This dashboard is available to parent accounts only." 
                });
            }

            // Verify password using authoritative AuthenticationService from Guardian.Core
            bool isValid = _authService.VerifyPassword(request.Password);
            if (!isValid)
            {
                // Increment failed attempts
                int count = attempt.Count + 1;
                DateTime lockout = count >= 5 ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue;
                _loginAttempts[rateKey] = (count, lockout);

                return Unauthorized(new LoginResponse 
                { 
                    Success = false, 
                    Message = "Invalid parent credentials. Access denied." 
                });
            }

            // Successful authentication -> Reset rate limit count
            _loginAttempts.TryRemove(rateKey, out _);

            // Generate JWT Token
            string token = GenerateJwtToken(request.Username);

            // Log successful auth in audit log
            _db.LogIncident("PARENT_WEB_LOGIN", request.Username, "Parent logged into Parent Web Dashboard.");

            return Ok(new LoginResponse
            {
                Success = true,
                Token = token,
                Username = request.Username,
                Message = "Authentication successful."
            });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            return Ok(new { success = true, message = "Logged out successfully." });
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Ok(new { username = User.Identity.Name, isParent = true });
            }
            return Unauthorized(new { message = "Not authenticated." });
        }

        private string GenerateJwtToken(string username)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(JwtSecret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, "Parent"),
                    new Claim("IsParent", "true")
                }),
                Expires = DateTime.UtcNow.AddHours(24),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
