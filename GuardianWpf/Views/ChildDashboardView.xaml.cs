using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class ChildAppUsageItem
    {
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string UsageAndLimitText { get; set; } = "";
        public double UsagePercent { get; set; } = 0;
        public string StatusBadgeText { get; set; } = "";
        public Brush BadgeBackground { get; set; } = Brushes.Transparent;
        public Brush BadgeForeground { get; set; } = Brushes.White;
        public Brush ProgressBrush { get; set; } = Brushes.DodgerBlue;
    }

    public partial class ChildDashboardView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;

        private bool _isInitializingCombo = true;

        public ChildDashboardView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;

            PopulateChildrenCombo();
            UpdateUI();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateUI);
        }

        private void PopulateChildrenCombo()
        {
            _isInitializingCombo = true;
            var children = _db.GetAllChildren();
            if (children.Count == 0)
            {
                children.Add(new ChildProfile { Id = "default", Name = "Alex" });
            }
            ActiveChildCombo.ItemsSource = children;
            ActiveChildCombo.SelectedValue = _usage.ActiveChildId;
            _isInitializingCombo = false;
        }

        private void ActiveChildCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingCombo || ActiveChildCombo.SelectedValue == null) return;
            string selectedId = ActiveChildCombo.SelectedValue.ToString() ?? "default";
            _usage.SetActiveChild(selectedId);
            UpdateUI();
        }

        public void UpdateUI()
        {
            long usedSecs = _usage.TotalDeviceSecondsToday;
            long remSecs = _usage.RemainingDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            TimeSpan remTs = TimeSpan.FromSeconds(remSecs);

            ChildUsedText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            ChildRemainingText.Text = $"{(int)remTs.TotalHours}h {remTs.Minutes:D2}m";
            ChildScheduleLabel.Text = $"Daily limit: {limitMins / 60}h {limitMins % 60:D2}m ({DateTime.Today.DayOfWeek})";

            long totalAllowedSecs = (limitMins * 60L) + _usage.CurrentDailyRecord.ManualBonusSeconds;
            if (totalAllowedSecs > 0)
            {
                double pct = Math.Min(100.0, (usedSecs / (double)totalAllowedSecs) * 100.0);
                ChildProgressBar.Value = pct;
                ChildProgressBar.Foreground = pct >= 90
                    ? ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson)
                    : ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            }

            // Load child app rules & usage
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

            ChildAppsList.ItemsSource = items;
        }

        private void RequestTime_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ChildRequestDialog(_db) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }
}
