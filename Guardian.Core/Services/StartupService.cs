using System;
using System.IO;
using Microsoft.Win32;

namespace Guardian.Core.Services
{
    public interface IStartupService
    {
        bool IsStartupEnabled();
        void EnableStartup(string appPath);
        void DisableStartup();
        void SyncStartupState(bool enable);
        void CreateDesktopShortcut();
    }

    public class StartupService : IStartupService
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "GuardianParentalControl";

        public bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, false);
                if (key != null)
                {
                    object? val = key.GetValue(AppRegistryName);
                    return val != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error reading Windows startup registry key", ex);
            }
            return false;
        }

        public void EnableStartup(string appPath)
        {
            try
            {
                if (!File.Exists(appPath)) return;

                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, true);
                if (key != null)
                {
                    string formattedCommand = $"\"{appPath}\" --autostart";
                    key.SetValue(AppRegistryName, formattedCommand);
                    Logger.Info($"Configured Windows startup registry key: {formattedCommand}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set Windows startup registry key", ex);
            }
        }

        public void DisableStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, true);
                if (key != null && key.GetValue(AppRegistryName) != null)
                {
                    key.DeleteValue(AppRegistryName, false);
                    Logger.Info("Removed Windows startup registry key.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to delete Windows startup registry key", ex);
            }
        }

        public void SyncStartupState(bool enable)
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

                if (enable)
                {
                    EnableStartup(exePath);
                }
                else
                {
                    DisableStartup();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error syncing startup state", ex);
            }
        }

        public void CreateDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Guardian.lnk");

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Path.Combine(baseDir, "GuardianWpf.exe");

                if (!File.Exists(exePath))
                {
                    string mainExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    if (mainExe.EndsWith("GuardianWpf.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exePath = mainExe;
                    }
                    else
                    {
                        string candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "GuardianWpf", "bin", "Debug", "net10.0-windows", "GuardianWpf.exe"));
                        if (File.Exists(candidate))
                        {
                            exePath = candidate;
                        }
                    }
                }

                if (!File.Exists(exePath)) return;

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = exePath;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                        shortcut.Description = "Guardian Parental Control System";
                        shortcut.Save();
                        Logger.Info($"Created Desktop shortcut at: {shortcutPath} -> {exePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to create Desktop shortcut", ex);
            }
        }
    }
}
