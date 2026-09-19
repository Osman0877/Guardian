using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class HistoryItemViewModel
    {
        public string DateLabel { get; set; } = "";
        public string FormattedUsed { get; set; } = "";
        public double UsagePercent { get; set; } = 0;
        public string StatusLabel { get; set; } = "";
    }

    public partial class ScreenTimeView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;
        private readonly IAuthenticationService? _auth;

        public event Action? OnInstantLockRequested;

        public ScreenTimeView(IDatabaseService db, IUsageTrackingService usage, IAuthenticationService? auth = null)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;
            _auth = auth;

            UpdateUI();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateUI);
        }

        private bool EnsureParentAuth()
        {
            if (_auth == null) return true;
            if (_auth.HasAuthenticatedSession) return true;

            var parentWin = Window.GetWindow(this);
            var dlg = new ParentLoginDialog(_auth) { Owner = parentWin };
            return dlg.ShowDialog() == true;
        }

        public void UpdateUI()
        {
            long usedSecs = _usage.TotalDeviceSecondsToday;
            long remSecs = _usage.RemainingDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            TimeSpan remTs = TimeSpan.FromSeconds(remSecs);

            UsedTimeText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            RemainingTimeText.Text = $"{(int)remTs.TotalHours}h {remTs.Minutes:D2}m";
            DailyAllowanceText.Text = $"{limitMins / 60}h {limitMins % 60:D2}m";

            long bonusSecs = _usage.CurrentDailyRecord.ManualBonusSeconds;
            BonusTimeLabel.Text = bonusSecs > 0 ? $"+{bonusSecs / 60}m bonus time granted" : "No bonus time applied";

            ScheduleNameText.Text = $"{DateTime.Today.DayOfWeek} Schedule";

            long totalAllowedSecs = (limitMins * 60L) + bonusSecs;
            if (totalAllowedSecs > 0)
            {
                double pct = Math.Min(100.0, (usedSecs / (double)totalAllowedSecs) * 100.0);
                UsageProgressBar.Value = pct;
                UsageProgressBar.Foreground = pct >= 90
                    ? ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson)
                    : ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            }

            // Load 7-day history from SQLite
            var pastDays = _db.GetUsageForDateRange(DateTime.Today.AddDays(-6), DateTime.Today, _usage.ActiveChildId);
            var historyItems = pastDays.Select(d =>
            {
                TimeSpan ts = TimeSpan.FromSeconds(d.TotalDeviceSecondsUsed);
                double pct = Math.Min(100.0, (d.TotalDeviceSecondsUsed / (double)(limitMins * 60.0)) * 100.0);
                return new HistoryItemViewModel
                {
                    DateLabel = DateTime.TryParse(d.DateString, out var dt) ? dt.ToString("ddd, MMM dd") : d.DateString,
                    FormattedUsed = $"{(int)ts.TotalHours}h {ts.Minutes:D2}m",
                    UsagePercent = pct,
                    StatusLabel = d.IsDeviceLocked ? "Locked" : "Completed"
                };
            }).ToList();

            HistoryListView.ItemsSource = historyItems;
        }

        private void Grant15_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureParentAuth()) _usage.GrantBonusMinutes(15);
        }

        private void Grant30_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureParentAuth()) _usage.GrantBonusMinutes(30);
        }

        private void Grant60_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureParentAuth()) _usage.GrantBonusMinutes(60);
        }

        private void GrantRestOfDay_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureParentAuth()) _usage.GrantBonusMinutes(720);
        }

        private void LockNow_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureParentAuth())
            {
                _usage.LockDeviceManually("Instant Lock by Parent");
                OnInstantLockRequested?.Invoke();
            }
        }
    }
}
