using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class AppDisplayItem
    {
        public string DisplayName { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string FormattedUsage { get; set; } = "";
        public string StatusLabel { get; set; } = "";
    }

    public partial class ParentDashboardView : UserControl
    {
        private readonly IUsageTrackingService _usage;
        private readonly IEnforcementService _enforcement;

        public event Action? OnInstantLockRequested;

        public ParentDashboardView(IUsageTrackingService usage, IEnforcementService enforcement)
        {
            InitializeComponent();
            _usage = usage;
            _enforcement = enforcement;

            AdminWarningBanner.Visibility = _enforcement.IsRunningAsWindowsAdmin ? Visibility.Visible : Visibility.Collapsed;

            UpdateUI();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateUI);
            _usage.OnAfkStateChanged += () => Dispatcher.Invoke(UpdateUI);
        }

        public void UpdateUI()
        {
            long usedSecs = _usage.TotalDeviceSecondsToday;
            long remSecs = _usage.RemainingDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            TimeSpan remTs = TimeSpan.FromSeconds(remSecs);

            ScreenTimeUsedText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            RemainingTimeText.Text = $"{(int)remTs.TotalHours}h {remTs.Minutes:D2}m";
            DailyLimitLabel.Text = $"Based on {limitMins / 60}h {limitMins % 60:D2}m schedule for {DateTime.Today.DayOfWeek}";

            long totalAllowedSecs = (limitMins * 60L) + _usage.CurrentDailyRecord.ManualBonusSeconds;
            if (totalAllowedSecs > 0)
            {
                double pct = Math.Min(100.0, (usedSecs / (double)totalAllowedSecs) * 100.0);
                ScreenTimeProgressBar.Value = pct;
                ScreenTimeProgressBar.Foreground = (pct >= 90.0)
                    ? ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson)
                    : ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            }

            ActiveAppText.Text = string.IsNullOrEmpty(_usage.ActiveProcessName) ? "None (Idle)" : _usage.ActiveProcessName;
            ActiveWindowTitleText.Text = string.IsNullOrEmpty(_usage.ActiveWindowTitle) ? "Desktop" : _usage.ActiveWindowTitle;

            // AFK Mode Status Card Update
            if (_usage.IsAfkModeActive)
            {
                AfkBadgeDot.Text = "🟢";
                AfkStatusTitleText.Text = "🟢 AFK MODE ACTIVE";
                AfkStatusTitleText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);

                string startStr = _usage.AfkStartedAt.HasValue ? _usage.AfkStartedAt.Value.ToString("h:mm tt") : "Unknown";
                TimeSpan remAfk = _usage.AfkTimeRemaining;
                string remText = remAfk == TimeSpan.MaxValue ? "Until manually disabled" : $"{(int)remAfk.TotalHours}h {remAfk.Minutes:D2}m remaining";

                AfkStatusDetailText.Text = $"Screen-time accounting is paused. Started: {startStr} | {remText}";
                AfkActionButton.Content = "🛑 Deactivate AFK Mode";
                AfkActionButton.Style = (Style)FindResource("DangerButtonStyle");
            }
            else
            {
                AfkBadgeDot.Text = "⚪";
                AfkStatusTitleText.Text = "⚪ AFK MODE INACTIVE";
                AfkStatusTitleText.Foreground = Brushes.White;
                AfkStatusDetailText.Text = "Screen-time accounting is currently active.";
                AfkActionButton.Content = "🟢 Activate AFK Mode";
                AfkActionButton.Style = (Style)FindResource("SuccessButtonStyle");
            }

            // Update top apps list
            var appList = _usage.CurrentDailyRecord.AppUsages.Values
                .OrderByDescending(a => a.SecondsUsed)
                .Take(6)
                .Select(a =>
                {
                    TimeSpan ts = TimeSpan.FromSeconds(a.SecondsUsed);
                    return new AppDisplayItem
                    {
                        DisplayName = a.DisplayName,
                        ProcessName = a.ProcessName,
                        FormattedUsage = $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s",
                        StatusLabel = "Active Today"
                    };
                })
                .ToList();

            TopAppsList.ItemsSource = appList;

            // Protection Status Indicator
            if (_enforcement.IsEnforcementActive && _enforcement.IsWatchdogRunning)
            {
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(22, 56, 40));
                StatusBadgeBorder.BorderBrush = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
                StatusDot.Fill = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
                StatusBadgeText.Text = "PROTECTED";
                StatusBadgeText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
            }
            else
            {
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(56, 36, 16));
                StatusBadgeBorder.BorderBrush = ThemeHelper.GetBrush("AccentGoldBrush", Brushes.Gold);
                StatusDot.Fill = ThemeHelper.GetBrush("AccentGoldBrush", Brushes.Gold);
                StatusBadgeText.Text = "ATTENTION";
                StatusBadgeText.Foreground = ThemeHelper.GetBrush("AccentGoldBrush", Brushes.Gold);
            }
        }

        private void AfkAction_Click(object sender, RoutedEventArgs e)
        {
            var parentWin = Window.GetWindow(this);
            var dlg = new AfkModeDialog(_usage, _usage.IsAfkModeActive) { Owner = parentWin };
            dlg.ShowDialog();
        }

        private void Grant15_Click(object sender, RoutedEventArgs e) => _usage.GrantBonusMinutes(15);
        private void Grant30_Click(object sender, RoutedEventArgs e) => _usage.GrantBonusMinutes(30);
        private void Grant60_Click(object sender, RoutedEventArgs e) => _usage.GrantBonusMinutes(60);

        private void InstantLock_Click(object sender, RoutedEventArgs e)
        {
            _usage.LockDeviceManually("Instant Lock by Parent");
            OnInstantLockRequested?.Invoke();
        }
    }
}
