using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Guardian.Core.Services
{
    public interface IFileProtectionService
    {
        void ApplyFileProtection(string targetDirectory);
        void ProtectFile(string filePath);
        void EnsureProtectedDesktopShortcut();
        bool IsRunningAsAdministrator();
    }

    public class FileProtectionService : IFileProtectionService
    {
        public bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public void ApplyFileProtection(string targetDirectory)
        {
            try
            {
                if (!Directory.Exists(targetDirectory)) return;

                DirectoryInfo dInfo = new DirectoryInfo(targetDirectory);
                DirectorySecurity dSecurity = dInfo.GetAccessControl();

                var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

                // Allow Administrators & SYSTEM Full Control
                dSecurity.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                dSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                // Restrict Standard Users to Read & Execute only (Deny Write, Delete, Modify)
                dSecurity.AddAccessRule(new FileSystemAccessRule(usersSid, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                dInfo.SetAccessControl(dSecurity);
                Logger.Info($"Applied Administrator-only file deletion protection on: {targetDirectory}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying file protection ACLs on {targetDirectory}", ex);
            }
        }

        public void ProtectFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                FileInfo fInfo = new FileInfo(filePath);
                FileSecurity fSecurity = fInfo.GetAccessControl();

                var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

                // Allow Administrators & SYSTEM Full Control
                fSecurity.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, AccessControlType.Allow));
                fSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));

                // Restrict Standard Users to Read & Execute only
                fSecurity.AddAccessRule(new FileSystemAccessRule(usersSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

                fInfo.SetAccessControl(fSecurity);
                Logger.Info($"Applied ACL protection on shortcut file: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error protecting file ACLs on {filePath}", ex);
            }
        }

        public void EnsureProtectedDesktopShortcut()
        {
            try
            {
                var startupService = new StartupService();
                startupService.CreateDesktopShortcut();

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Guardian.lnk");

                if (File.Exists(shortcutPath))
                {
                    ProtectFile(shortcutPath);
                }

                // Also check Public Desktop for all users
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (!string.IsNullOrEmpty(publicDesktop) && Directory.Exists(publicDesktop))
                {
                    string publicShortcut = Path.Combine(publicDesktop, "Guardian.lnk");
                    if (File.Exists(publicShortcut))
                    {
                        ProtectFile(publicShortcut);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error ensuring protected desktop shortcut", ex);
            }
        }
    }
}
