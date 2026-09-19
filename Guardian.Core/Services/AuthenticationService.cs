using System;
using System.Security.Cryptography;
using System.Text;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface IAuthenticationService
    {
        bool IsDefaultPassword { get; }
        bool HasAuthenticatedSession { get; }
        DateTime? SessionExpiresAt { get; }

        bool VerifyPassword(string password);
        bool SetNewPassword(string currentPassword, string newPassword, out string errorMessage);
        bool AuthenticateAndStartSession(string password);
        void EndSession();
        bool IsActionAuthorized(string? explicitPassword = null);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private const string DefaultAdminPassword = "1111";
        private const int Pbkdf2Iterations = 100000;
        private const int SaltByteSize = 32;
        private const int HashByteSize = 32;

        private readonly IDatabaseService _db;
        private GuardianSettings _settings;
        private DateTime? _sessionExpiry = null;

        public bool IsDefaultPassword => _settings.IsDefaultPassword;
        public bool HasAuthenticatedSession => _sessionExpiry.HasValue && DateTime.Now < _sessionExpiry.Value;
        public DateTime? SessionExpiresAt => _sessionExpiry;

        public AuthenticationService(IDatabaseService db)
        {
            _db = db;
            _settings = _db.LoadSettings();

            // Initialize default password "1111" if not yet created
            if (string.IsNullOrEmpty(_settings.PasswordHash) || string.IsNullOrEmpty(_settings.PasswordSalt))
            {
                InitializeDefaultPassword();
            }
        }

        private void InitializeDefaultPassword()
        {
            byte[] salt = GenerateSalt();
            byte[] hash = HashPassword(DefaultAdminPassword, salt);

            _settings.PasswordSalt = Convert.ToBase64String(salt);
            _settings.PasswordHash = Convert.ToBase64String(hash);
            _settings.IsDefaultPassword = true;

            _db.SaveSettings(_settings);
            Logger.Info("Initialized default administrator password ('1111').");
        }

        public bool VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;

            try
            {
                byte[] salt = Convert.FromBase64String(_settings.PasswordSalt);
                byte[] expectedHash = Convert.FromBase64String(_settings.PasswordHash);
                byte[] actualHash = HashPassword(password, salt);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (Exception ex)
            {
                Logger.Error("Error during password verification", ex);
                return false;
            }
        }

        public bool AuthenticateAndStartSession(string password)
        {
            if (VerifyPassword(password))
            {
                int timeoutMins = Math.Max(5, _settings.SessionTimeoutMinutes);
                _sessionExpiry = DateTime.Now.AddMinutes(timeoutMins);
                Logger.Info($"Parent session authenticated successfully (active for {timeoutMins} minutes).");
                return true;
            }

            Logger.Warn("Failed parent authentication attempt.");
            return false;
        }

        public void EndSession()
        {
            _sessionExpiry = null;
            Logger.Info("Parent session closed.");
        }

        public bool IsActionAuthorized(string? explicitPassword = null)
        {
            // If an explicit password was passed, verify it directly
            if (!string.IsNullOrEmpty(explicitPassword))
            {
                return VerifyPassword(explicitPassword);
            }

            // Otherwise, check if an active parent authenticated session is running
            return HasAuthenticatedSession;
        }

        public bool SetNewPassword(string currentPassword, string newPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!VerifyPassword(currentPassword))
            {
                errorMessage = "Current password is incorrect.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            {
                errorMessage = "New password must be at least 4 characters long.";
                return false;
            }

            byte[] newSalt = GenerateSalt();
            byte[] newHash = HashPassword(newPassword, newSalt);

            _settings.PasswordSalt = Convert.ToBase64String(newSalt);
            _settings.PasswordHash = Convert.ToBase64String(newHash);
            _settings.IsDefaultPassword = (newPassword == DefaultAdminPassword);

            _db.SaveSettings(_settings);
            Logger.Info("Parent password successfully updated.");

            // Refresh session
            int timeoutMins = Math.Max(5, _settings.SessionTimeoutMinutes);
            _sessionExpiry = DateTime.Now.AddMinutes(timeoutMins);

            return true;
        }

        private static byte[] GenerateSalt()
        {
            byte[] salt = new byte[SaltByteSize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);
            return salt;
        }

        private static byte[] HashPassword(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashByteSize);
        }
    }
}
