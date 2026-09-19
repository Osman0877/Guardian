using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Guardian.Core.Win32;
using GuardianWpf.Views;

namespace GuardianWpf
{
    public partial class App : Application
    {
        private const string AppMutexName = @"Local\GuardianParentalControlApp_Mutex_v3";
        private const string ShowEventName = @"Local\GuardianParentalControlApp_ShowEvent_v3";

        private static Mutex? _mutex;
        private static EventWaitHandle? _showEvent;

        private IDatabaseService? _db;
        private IAuthenticationService? _auth;
        private IUsageTrackingService? _usage;
        private IEnforcementService? _enforcement;
        private IWebProtectionService? _web;

        private MainWindow? _mainWindow;
        private LockScreenWindow? _lockScreen;
        private GuardianTrayIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup global exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Logger.Error("AppDomain Unhandled Exception", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
            };

            DispatcherUnhandledException += (s, args) =>
            {
                Logger.Error("Dispatcher Unhandled Exception", args.Exception);
                args.Handled = true;
            };

            Logger.Info("=== GuardianWpf Application Launching ===");

            // 1. Single Instance Check
            bool isNewInstance = false;
            try
            {
                _mutex = new Mutex(true, AppMutexName, out isNewInstance);
            }
            catch (AbandonedMutexException)
            {
                isNewInstance = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating mutex", ex);
                isNewInstance = true;
            }

            if (!isNewInstance)
            {
                Logger.Info("Another Guardian instance is already running. Signaling show event...");
                try
                {
                    using var evt = EventWaitHandle.OpenExisting(ShowEventName);
                    evt.Set();
                }
                catch { }

                var existing = Process.GetProcessesByName("GuardianWpf").Where(p => p.Id != Environment.ProcessId).ToList();
                if (existing.Count > 0)
                {
                    Shutdown();
                    return;
                }
            }

            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Task.Run(() =>
                {
                    while (true)
                    {
                        try
                        {
                            _showEvent.WaitOne();
                            Dispatcher.Invoke(ShowMainWindow);
                        }
                        catch { break; }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Error setting up ShowEventHandle", ex);
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 2. Initialize Core Services
            try
            {
                _db = new DatabaseService();
                _auth = new AuthenticationService(_db);
                _usage = new UsageTrackingService(_db);
                _enforcement = new EnforcementService(_db, _usage);
                _web = new WebProtectionService(_db);

                var fileProtection = new FileProtectionService();
                fileProtection.ApplyFileProtection(AppDomain.CurrentDomain.BaseDirectory);
                fileProtection.EnsureProtectedDesktopShortcut();

                _usage.Start();
                _enforcement.Start();

                _usage.OnDeviceLimitReached += () => Dispatcher.Invoke(TriggerDeviceLock);
                _enforcement.OnDeviceLockTriggered += () => Dispatcher.Invoke(TriggerDeviceLock);

                var settings = _db.LoadSettings();
                if (settings.ActiveRole == UserRole.NotSelected)
                {
                    settings.ActiveRole = UserRole.Parent;
                    _db.SaveSettings(settings);
                }

                // 3. Create Main Window
                _mainWindow = new MainWindow(_db, _auth, _usage, _enforcement, _web);
                MainWindow = _mainWindow;

                ShowMainWindow();
                InitializeSystemTray();

                Logger.Info("Guardian UI loaded and active.");
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal exception during startup", ex);
                MessageBox.Show($"Startup Error: {ex.Message}\n\n{ex.StackTrace}", "Guardian Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            try
            {
                _mainWindow.Visibility = Visibility.Visible;
                _mainWindow.Show();
                if (_mainWindow.WindowState == WindowState.Minimized)
                {
                    _mainWindow.WindowState = WindowState.Normal;
                }

                var helper = new WindowInteropHelper(_mainWindow);
                IntPtr hwnd = helper.EnsureHandle();

                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);

                _mainWindow.Activate();
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
                _mainWindow.Focus();
            }
            catch (Exception ex)
            {
                Logger.Error("Error showing main window", ex);
            }
        }

        public void TriggerDeviceLock()
        {
            if (_auth == null || _usage == null || _db == null) return;

            if (!_usage.IsDeviceLimitReached)
            {
                _usage.LockDeviceManually("Device Lock Activated");
            }

            if (_lockScreen != null && _lockScreen.IsVisible)
            {
                _lockScreen.Activate();
                _lockScreen.Topmost = true;
                return;
            }

            try
            {
                _lockScreen = new LockScreenWindow(_auth, _usage, _db);
                _lockScreen.OnUnlocked += () =>
                {
                    _lockScreen?.Close();
                    _lockScreen = null;
                    ShowMainWindow();
                };
                _lockScreen.Show();
                _lockScreen.Activate();
                _lockScreen.Topmost = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Error showing lock screen", ex);
            }
        }

        private void InitializeSystemTray()
        {
            try
            {
                IntPtr hwnd = IntPtr.Zero;
                if (_mainWindow != null)
                {
                    var helper = new WindowInteropHelper(_mainWindow);
                    hwnd = helper.EnsureHandle();
                }

                IntPtr hIcon = IntPtr.Zero;
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (File.Exists(exePath))
                    {
                        var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (icon != null) hIcon = icon.Handle;
                    }
                }
                catch { }

                _trayIcon = new GuardianTrayIcon("Guardian Parental Controls", hwnd, hIcon);
                _trayIcon.OnTrayClicked += () => Dispatcher.Invoke(ShowMainWindow);
                _trayIcon.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("Error initializing system tray", ex);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _trayIcon?.Dispose();
                _usage?.Stop();
                _enforcement?.Stop();
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
