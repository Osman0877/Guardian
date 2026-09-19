using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Guardian.Core.Win32;

namespace GuardianWpf.Views
{
    public partial class LockScreenWindow : Window
    {
        private readonly IAuthenticationService _auth;
        private readonly IUsageTrackingService _usage;
        private readonly IDatabaseService _db;
        private readonly DispatcherTimer _guardTimer;

        private IntPtr _hookId = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _proc;

        public event Action? OnUnlocked;

        public LockScreenWindow(IAuthenticationService auth, IUsageTrackingService usage, IDatabaseService db)
        {
            InitializeComponent();
            _auth = auth;
            _usage = usage;
            _db = db;

            // Multi-monitor full-screen overlay geometry
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            Topmost = true;
            WindowState = WindowState.Maximized;

            UpdateDisplay();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateDisplay);

            // Install Low-Level Keyboard Hook to suppress Alt+Tab, Alt+F4, Win key, etc.
            InstallKeyboardHook();

            // Periodic Guard Timer (200ms) to ensure window remains focused, topmost, and full-screen
            _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _guardTimer.Tick += (s, e) => EnsureTopmostFocus();
            _guardTimer.Start();

            Deactivated += (s, e) => EnsureTopmostFocus();
            Closing += (s, e) =>
            {
                // Prevent closing if still locked and not parent authorized
                if (_usage.IsDeviceLimitReached && !_auth.HasAuthenticatedSession)
                {
                    e.Cancel = true;
                }
                else
                {
                    RemoveKeyboardHook();
                    _guardTimer.Stop();
                }
            };
        }

        private void EnsureTopmostFocus()
        {
            if (_usage.IsDeviceLimitReached && !_auth.HasAuthenticatedSession)
            {
                Topmost = false;
                Topmost = true;
                Activate();

                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.BringWindowToTop(hwnd);
                }
            }
        }

        private void InstallKeyboardHook()
        {
            try
            {
                _proc = HookCallback;
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = curModule != null ? NativeMethods.GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;

                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
            }
            catch (Exception ex)
            {
                Logger.Error("Error installing low-level keyboard hook", ex);
            }
        }

        private void RemoveKeyboardHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _usage.IsDeviceLimitReached && !_auth.HasAuthenticatedSession)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                uint vkCode = hookStruct.vkCode;
                bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || (hookStruct.flags & 0x20) != 0;
                bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

                // Intercept and swallow escape keys: Win Keys, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4, Ctrl+Shift+Esc
                if (vkCode == NativeMethods.VK_LWIN || vkCode == NativeMethods.VK_RWIN) return (IntPtr)1;
                if (vkCode == NativeMethods.VK_TAB && isAlt) return (IntPtr)1;
                if (vkCode == NativeMethods.VK_ESCAPE && (isAlt || isCtrl)) return (IntPtr)1;
                if (vkCode == NativeMethods.VK_F4 && isAlt) return (IntPtr)1;
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void UpdateDisplay()
        {
            long usedSecs = _usage.TotalDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            UsageText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            LimitText.Text = $"{limitMins / 60}h {limitMins % 60:D2}m";
            LockSubheaderText.Text = _usage.CurrentRestrictionReason;

            // If time was granted and limit is no longer reached, close lock screen
            if (!_usage.IsDeviceLimitReached)
            {
                RemoveKeyboardHook();
                _guardTimer.Stop();
                Hide();
            }
        }

        private void ParentUnlock_Click(object sender, RoutedEventArgs e)
        {
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            UnlockFormPanel.Visibility = Visibility.Visible;
            UnlockPasswordBox.Password = "";
            UnlockErrorText.Visibility = Visibility.Collapsed;
            UnlockPasswordBox.Focus();
        }

        private void CancelUnlock_Click(object sender, RoutedEventArgs e)
        {
            UnlockFormPanel.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Visibility = Visibility.Visible;
        }

        private void RequestTime_Click(object sender, RoutedEventArgs e)
        {
            var req = new ChildRequest
            {
                Type = RequestType.ExtraScreenTime,
                Title = "Extra Screen Time Request",
                Reason = "Child requested 30 minutes of additional screen time from device lock screen.",
                RequestedMinutes = 30,
                Status = RequestStatus.Pending
            };
            _db.SaveRequest(req);
            _db.LogIncident("CHILD_REQUEST_SUBMITTED", "System", "Child submitted a request for +30m screen time.");

            MessageBox.Show("Your request for 30 more minutes was sent to your parents!", "Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void GrantTime15_Click(object sender, RoutedEventArgs e) => TryGrantTime(15);
        private void GrantTime30_Click(object sender, RoutedEventArgs e) => TryGrantTime(30);
        private void GrantTime60_Click(object sender, RoutedEventArgs e) => TryGrantTime(60);
        private void GrantRestOfDay_Click(object sender, RoutedEventArgs e)
        {
            string pwd = UnlockPasswordBox.Password;
            if (_auth.VerifyPassword(pwd) || _auth.HasAuthenticatedSession)
            {
                _auth.AuthenticateAndStartSession(pwd);
                _usage.UnlockDeviceForToday();
                OnUnlocked?.Invoke();
                Hide();
            }
            else
            {
                UnlockErrorText.Text = "Incorrect parent password. Please try again.";
                UnlockErrorText.Visibility = Visibility.Visible;
                UnlockPasswordBox.SelectAll();
                UnlockPasswordBox.Focus();
            }
        }

        private void TryGrantTime(int minutes)
        {
            string pwd = UnlockPasswordBox.Password;
            if (_auth.VerifyPassword(pwd) || _auth.HasAuthenticatedSession)
            {
                _auth.AuthenticateAndStartSession(pwd);
                _usage.GrantBonusMinutes(minutes);
                _db.LogIncident("PARENT_UNLOCK_GRANTED", "Parent", $"Parent granted +{minutes}m extension.");
                OnUnlocked?.Invoke();
                Hide();
            }
            else
            {
                UnlockErrorText.Text = "Incorrect parent password. Please try again.";
                UnlockErrorText.Visibility = Visibility.Visible;
                UnlockPasswordBox.SelectAll();
                UnlockPasswordBox.Focus();
            }
        }

        private void UnlockPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryGrantTime(30); // Default to +30m on Enter
            }
        }
    }
}
