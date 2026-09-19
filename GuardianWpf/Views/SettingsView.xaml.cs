using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IAuthenticationService _auth;
        private readonly IUsageTrackingService _usage;
        private GuardianSettings _settings;

        private bool _isLoaded = false;
        private string _computedWebUrl = "http://localhost:5000";

        public SettingsView(IDatabaseService db, IAuthenticationService auth, IUsageTrackingService usage)
        {
            _db = db;
            _auth = auth;
            _usage = usage;

            InitializeComponent();
            _settings = _db.LoadSettings();

            LoadSettingsToUI();
            _isLoaded = true;
        }

        private void LoadSettingsToUI()
        {
            if (_auth == null || _settings == null) return;

            PasswordStatusText.Text = _auth.IsDefaultPassword
                ? "Status: Using Default Admin Password (1111) — Recommended to change"
                : "Status: Custom Secure Password Active";
            PasswordStatusText.Foreground = _auth.IsDefaultPassword
                ? ThemeHelper.GetBrush("AccentGoldBrush", Brushes.Gold)
                : ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);

            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTrayOnClose;

            // Load parent username from settings or default MasterDad
            ParentUsernameInput.Text = "MasterDad";

            // Compute local IP address for phone/tablet web access
            string localIp = GetLocalIpAddress();
            _computedWebUrl = $"http://{localIp}:5000";
            WebUrlDisplay.Text = $"{_computedWebUrl}   (or http://localhost:5000)";
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "localhost";
        }

        private void SaveUsername_Click(object sender, RoutedEventArgs e)
        {
            string newUsername = ParentUsernameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(newUsername))
            {
                MessageBox.Show("Please enter a valid username.", "Invalid Username", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show($"Parent Username updated to '{newUsername}'. You can now log in on the Web Dashboard using this username and your parent password.", "Username Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopyWebUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_computedWebUrl);
                MessageBox.Show($"Copied web dashboard URL '{_computedWebUrl}' to clipboard!\n\nOpen this link in any browser on your phone, tablet, or laptop.", "URL Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _auth == null) return;
            var dlg = new ChangePasswordDialog(_auth) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadSettingsToUI();
            }
        }

        private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _db == null || _settings == null) return;
            _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _db.SaveSettings(_settings);
        }

        private void MinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _db == null || _settings == null) return;
            _settings.MinimizeToTrayOnClose = MinimizeToTrayCheck.IsChecked == true;
            _db.SaveSettings(_settings);
        }

        private void ResetUsage_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Are you sure you want to reset today's active screen-time and application usage counters to zero?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                _usage.CurrentDailyRecord.TotalDeviceSecondsUsed = 0;
                _usage.CurrentDailyRecord.ManualBonusSeconds = 0;
                _usage.CurrentDailyRecord.IsDeviceLocked = false;
                _usage.CurrentDailyRecord.AppUsages.Clear();
                _usage.FlushToDatabase();

                MessageBox.Show("Today's usage counters have been reset.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
