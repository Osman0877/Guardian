using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWatchdog
{
    internal class Program
    {
        private static bool _isRunning = true;

        private static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(" 🛡️ GUARDIAN WATCHDOG RECOVERY SERVICE");
            Console.WriteLine(" Monitoring GuardianWpf.exe process state (1.0s loop)");
            Console.WriteLine("==================================================");

            Logger.Info("GuardianWatchdog service started.");

            Console.CancelKeyPress += (s, e) =>
            {
                _isRunning = false;
                Logger.Info("GuardianWatchdog service stopping.");
            };

            var fileProtection = new FileProtectionService();

            while (_isRunning)
            {
                try
                {
                    Process[] wpfProcesses = Process.GetProcessesByName("GuardianWpf");
                    if (wpfProcesses.Length == 0)
                    {
                        Logger.Warn("Watchdog detected GuardianWpf.exe is NOT running. Initiating recovery...");
                        RecoverGuardianWpf();
                    }

                    // Auto-heal desktop shortcut if removed
                    fileProtection.EnsureProtectedDesktopShortcut();
                }
                catch (Exception ex)
                {
                    Logger.Error("Error in Watchdog loop", ex);
                }

                Thread.Sleep(1100); // 1.1s poll rate
            }
        }

        private static void RecoverGuardianWpf()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string wpfExe = Path.Combine(baseDir, "GuardianWpf.exe");

                // In debug or test environments, check solution directory
                if (!File.Exists(wpfExe))
                {
                    string fallback = Path.GetFullPath(Path.Combine(baseDir, "..\\..\\..\\..\\GuardianWpf\\bin\\Debug\\net10.0-windows\\GuardianWpf.exe"));
                    if (File.Exists(fallback)) wpfExe = fallback;
                }

                if (File.Exists(wpfExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = wpfExe,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(wpfExe)!
                    };

                    Process.Start(psi);
                    Logger.Info($"GuardianWatchdog successfully recovered GuardianWpf at: {wpfExe}");
                    Console.WriteLine($"[RECOVERED] Launched {wpfExe}.");

                    try
                    {
                        var db = new DatabaseService();
                        db.LogIncident("WATCHDOG_RECOVERY", "GuardianWatchdog", $"Watchdog detected main UI process termination and successfully restarted {wpfExe}");
                        db.SaveNotification(new GuardianNotification
                        {
                            Title = "Watchdog Process Recovery",
                            Message = "Guardian Protection Engine auto-recovered main application process after termination.",
                            Severity = NotificationSeverity.Warning
                        });
                    }
                    catch { }

                    Thread.Sleep(1500); // Wait for initialization
                }
                else
                {
                    Logger.Error($"Watchdog recovery failed: GuardianWpf.exe not found at {wpfExe}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during GuardianWpf recovery", ex);
            }
        }
    }
}
