using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
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

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class CreateChildRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int Age { get; set; } = 10;
        public int DailyLimitMinutes { get; set; } = 120;
    }

    public class RegisterDeviceAccountRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
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

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Username and password are required." });
            }

            try
            {
                var user = _authService.RegisterUser(request.Username, request.Password, UserRole.Parent);
                _db.LogIncident("PARENT_REGISTER", user.Username, $"Registered new Parent account '{user.Username}'.");
                return Ok(new { success = true, userId = user.Id, message = "Parent account created successfully. Please log in." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("register-device-account")]
        public IActionResult RegisterDeviceAccount([FromBody] RegisterDeviceAccountRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Username and password are required." });
            }

            if (request.Password.Trim().Length < 4)
            {
                return BadRequest(new { success = false, message = "Password must be at least 4 characters long." });
            }

            string cleanUsername = request.Username.Trim();
            var existingUser = _db.GetUserByUsername(cleanUsername);

            UserAccount user;
            if (existingUser != null)
            {
                var verified = _authService.VerifyUserCredentials(cleanUsername, request.Password);
                if (verified == null)
                {
                    return BadRequest(new { success = false, message = $"Username '{cleanUsername}' is already taken. Please choose a unique username." });
                }
                user = verified;
            }
            else
            {
                try
                {
                    user = _authService.RegisterUser(cleanUsername, request.Password, UserRole.Parent);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { success = false, message = ex.Message });
                }
            }

            string deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? Environment.MachineName : request.DeviceId.Trim();
            string deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? deviceId : request.DeviceName.Trim();

            var deviceStatus = _db.GetDeviceStatus(deviceId) ?? new DeviceStatusRecord
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            };

            deviceStatus.ParentId = user.Id;
            _db.SaveDeviceStatus(deviceStatus);

            _db.LogIncident("DEVICE_ACCOUNT_LINKED", user.Username, $"Linked device '{deviceId}' to user account '{user.Username}'.");

            string token = GenerateJwtToken(user);
            return Ok(new
            {
                success = true,
                token = token,
                userId = user.Id,
                username = user.Username,
                deviceId = deviceId,
                message = $"Account '{user.Username}' registered & device connected successfully!"
            });
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponse { Success = false, Message = "Username and password are required." });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string rateKey = $"{clientIp}_{request.Username.Trim().ToLowerInvariant()}";

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

            // Real credential verification against database
            var user = _authService.VerifyUserCredentials(request.Username, request.Password);
            if (user == null)
            {
                int count = attempt.Count + 1;
                DateTime lockout = count >= 5 ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue;
                _loginAttempts[rateKey] = (count, lockout);

                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "Invalid username or password. Access denied."
                });
            }

            _loginAttempts.TryRemove(rateKey, out _);

            string token = GenerateJwtToken(user);
            _db.LogIncident("USER_LOGIN", user.Username, $"{user.Role} logged in successfully.");

            return Ok(new LoginResponse
            {
                Success = true,
                Token = token,
                UserId = user.Id,
                Username = user.Username,
                Role = user.Role.ToString(),
                Message = "Authentication successful."
            });
        }

        [HttpPost("create-child")]
        public IActionResult CreateChild([FromBody] CreateChildRequest request)
        {
            string parentId = GetAuthenticatedParentId();
            if (string.IsNullOrEmpty(parentId))
            {
                return Unauthorized(new { success = false, message = "Parent authorization required." });
            }

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Child name, username, and password are required." });
            }

            try
            {
                var childUser = _authService.RegisterUser(request.Username, request.Password, UserRole.Child, parentId);
                var childProfile = new ChildProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = parentId,
                    UserId = childUser.Id,
                    Name = request.Name.Trim(),
                    Age = request.Age,
                    DailyLimitMinutes = request.DailyLimitMinutes,
                    IsActive = true
                };

                _db.SaveChild(childProfile);
                _db.LogIncident("CHILD_CREATED", childUser.Username, $"Parent '{parentId}' created child profile '{request.Name}'.");

                return Ok(new { success = true, childId = childProfile.Id, message = "Child profile created successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
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
                string userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                string role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                return Ok(new { userId, username = User.Identity.Name, role, isParent = role == "Parent" });
            }
            return Unauthorized(new { message = "Not authenticated." });
        }

        private string GenerateJwtToken(UserAccount user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(JwtSecret);
            string parentId = user.Role == UserRole.Parent ? user.Id : user.ParentId;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role.ToString()),
                    new Claim("ParentId", parentId),
                    new Claim("UserId", user.Id)
                }),
                Expires = DateTime.UtcNow.AddHours(24),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private string GetAuthenticatedParentId()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
                if (roleClaim == "Parent")
                {
                    return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                }
            }
            return string.Empty;
        }
    }
}
