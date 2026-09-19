using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public partial class ChildHudView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;

        public ChildHudView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;

            UpdateUI();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateUI);
            _usage.OnAfkStateChanged += () => Dispatcher.Invoke(UpdateUI);
        }

        public void UpdateUI()
        {
            var child = _db.GetChild(_usage.ActiveChildId) ?? new ChildProfile { Name = "Child", Avatar = "👦" };
            ChildGreetingText.Text = $"Hello, {child.Name}!";
            ChildAvatarText.Text = child.Avatar;

            if (_usage.IsAfkModeActive)
            {
                ChildAfkBanner.Visibility = Visibility.Visible;
                string startStr = _usage.AfkStartedAt.HasValue ? _usage.AfkStartedAt.Value.ToString("h:mm tt") : "";
                ChildAfkText.Text = string.IsNullOrEmpty(startStr)
                    ? "Screen-time accounting is paused."
                    : $"Screen-time accounting is paused. (Started at {startStr})";
            }
            else
            {
                ChildAfkBanner.Visibility = Visibility.Collapsed;
            }

            long usedSecs = _usage.TotalDeviceSecondsToday;
            long remSecs = _usage.RemainingDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            TimeSpan remTs = TimeSpan.FromSeconds(remSecs);

            UsedText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            RemainingText.Text = $"{(int)remTs.TotalHours}h {remTs.Minutes:D2}m";

            var sched = _usage.CurrentSchedule;
            BedtimeNoticeText.Text = sched.BedtimeEnabled ? $"Bedtime restriction is at {sched.Bedtime:hh\\:mm}" : "Bedtime: None";

            long totalAllowedSecs = (limitMins * 60L) + _usage.CurrentDailyRecord.ManualBonusSeconds;
            if (totalAllowedSecs > 0)
            {
                double pct = Math.Min(100.0, (usedSecs / (double)totalAllowedSecs) * 100.0);
                UsageBar.Value = pct;
                UsageBar.Foreground = pct >= 90
                    ? ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson)
                    : ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            }

            var rules = _db.GetAllAppRules();
            var dailyUsage = _usage.CurrentDailyRecord;

            var items = rules.Select(r =>
            {
                dailyUsage.AppUsages.TryGetValue(r.ProcessName, out var usageEntry);
                long sec = usageEntry?.SecondsUsed ?? 0;
                TimeSpan appTs = TimeSpan.FromSeconds(sec);

                string usageStr = $"{(int)appTs.TotalHours}h {appTs.Minutes:D2}m";
                string limitStr = r.DailyLimitMinutes > 0 ? $" / {r.DailyLimitMinutes / 60}h {r.DailyLimitMinutes % 60:D2}m" : " (Unlimited)";
                double appPct = r.DailyLimitMinutes > 0 ? Math.Min(100.0, (sec / (double)(r.DailyLimitMinutes * 60.0)) * 100.0) : 0;

                string badge = r.IsBlocked ? "BLOCKED" : (r.DailyLimitMinutes > 0 && appPct >= 100 ? "TIME UP" : "ALLOWED");
                Brush bg = r.IsBlocked || (r.DailyLimitMinutes > 0 && appPct >= 100) ? new SolidColorBrush(Color.FromRgb(56, 20, 30)) : new SolidColorBrush(Color.FromRgb(22, 56, 40));
                Brush fg = r.IsBlocked || (r.DailyLimitMinutes > 0 && appPct >= 100) ? new SolidColorBrush(Color.FromRgb(255, 61, 113)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));

                return new ChildAppUsageItem
                {
                    DisplayName = r.DisplayName,
                    Category = r.Category,
                    UsageAndLimitText = usageStr + limitStr,
                    UsagePercent = appPct,
                    StatusBadgeText = badge,
                    BadgeBackground = bg,
                    BadgeForeground = fg,
                    ProgressBrush = appPct >= 90 ? (Brush)FindResource("DangerRedBrush") : (Brush)FindResource("AccentBlueBrush")
                };
            }).ToList();

            ChildAppsListView.ItemsSource = items;
        }

        private void ChildNav_Click(object sender, RoutedEventArgs e)
        {
            UpdateUI();
        }

        private void RequestTime_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ChildRequestDialog(_db) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }
}
