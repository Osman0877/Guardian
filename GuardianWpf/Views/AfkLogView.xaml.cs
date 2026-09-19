using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Guardian.Core.Models;
using Guardian.Core.Services;
using Microsoft.Win32;

namespace GuardianWpf.Views
{
    public partial class AfkLogView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;
        private readonly DispatcherTimer _activeTimer;

        public AfkLogView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;

            _activeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _activeTimer.Tick += (s, e) => UpdateActiveCard();
            _activeTimer.Start();

            _usage.OnAfkStateChanged += () => Dispatcher.Invoke(LoadData);
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateActiveCard);

            LoadData();
        }

        public void LoadData()
        {
            // 1. Load Summary Statistics
            var (todayCount, todaySecs, weekCount, weekSecs, totalCount, totalSecs) = _db.GetAfkSummaryStats();

            TimeSpan todayTs = TimeSpan.FromSeconds(todaySecs);
            TimeSpan weekTs = TimeSpan.FromSeconds(weekSecs);
            TimeSpan totalTs = TimeSpan.FromSeconds(totalSecs);

            AfkTimeTodayText.Text = $"{(int)todayTs.TotalHours}h {todayTs.Minutes:D2}m";
            AfkCountTodayText.Text = $"{todayCount} AFK Session{(todayCount == 1 ? "" : "s")} Today";

            AfkTimeWeekText.Text = $"{(int)weekTs.TotalHours}h {weekTs.Minutes:D2}m";
            AfkCountWeekText.Text = $"{weekCount} AFK Session{(weekCount == 1 ? "" : "s")} This Week";

            AfkTimeTotalText.Text = $"{(int)totalTs.TotalHours}h {totalTs.Minutes:D2}m";
            AfkCountTotalText.Text = $"{totalCount} Total Session{(totalCount == 1 ? "" : "s")}";

            // 2. Update Active Card
            UpdateActiveCard();

            // 3. Load & Filter Historical Sessions Table
            ApplyFilterAndSort();
        }

        private void UpdateActiveCard()
        {
            if (_usage.IsAfkModeActive)
            {
                ActiveAfkCard.Visibility = Visibility.Visible;
                DateTime? started = _usage.AfkStartedAt;
                if (started.HasValue)
                {
                    ActiveStartedText.Text = $"Started: {started.Value:MMMM dd, yyyy} at {started.Value:h:mm:ss tt}";
                    TimeSpan elapsed = DateTime.Now - started.Value;
                    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                    ActiveDurationText.Text = $"Current Duration: {(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
                }
                else
                {
                    ActiveStartedText.Text = "Started: Recently";
                    ActiveDurationText.Text = "Current Duration: Active";
                }
            }
            else
            {
                ActiveAfkCard.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilterAndSort()
        {
            var sessions = _db.GetAllAfkSessions();
            if (sessions == null) sessions = new List<AfkSessionRecord>();

            // Date Filtering
            string dateOption = (DateFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Time";
            DateTime todayDate = DateTime.Today;

            IEnumerable<AfkSessionRecord> filtered = dateOption switch
            {
                "Today" => sessions.Where(s => s.LocalStartTime.Date == todayDate),
                "Yesterday" => sessions.Where(s => s.LocalStartTime.Date == todayDate.AddDays(-1)),
                "Last 7 Days" => sessions.Where(s => s.LocalStartTime.Date >= todayDate.AddDays(-6)),
                "Last 30 Days" => sessions.Where(s => s.LocalStartTime.Date >= todayDate.AddDays(-29)),
                _ => sessions
            };

            // Sorting
            string sortOption = (SortCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Newest First";
            List<AfkSessionRecord> sorted = sortOption switch
            {
                "Oldest First" => filtered.OrderBy(s => s.StartTimestamp).ToList(),
                "Longest Duration" => filtered.OrderByDescending(s => s.CalculatedDuration).ToList(),
                "Shortest Duration" => filtered.OrderBy(s => s.CalculatedDuration).ToList(),
                _ => filtered.OrderByDescending(s => s.StartTimestamp).ToList()
            };

            AfkSessionsList.ItemsSource = sorted;
            NoSessionsText.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndSort();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private void SessionDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AfkSessionRecord session)
            {
                OpenSessionDetails(session);
            }
        }

        private void AfkSessionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AfkSessionsList.SelectedItem is AfkSessionRecord session)
            {
                OpenSessionDetails(session);
                AfkSessionsList.SelectedItem = null;
            }
        }

        private void OpenSessionDetails(AfkSessionRecord session)
        {
            var win = Window.GetWindow(this);
            var dlg = new AfkSessionDetailDialog(session) { Owner = win };
            dlg.ShowDialog();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sessions = _db.GetAllAfkSessions();
                var dlg = new SaveFileDialog
                {
                    Filter = "CSV File (*.csv)|*.csv",
                    FileName = $"Guardian_AFK_Log_{DateTime.Today:yyyyMMdd}.csv",
                    Title = "Export AFK Log to CSV"
                };

                if (dlg.ShowDialog() == true)
                {
                    using var writer = new StreamWriter(dlg.FileName);
                    writer.WriteLine("\"Session ID\",\"Start Date\",\"Start Time\",\"End Date\",\"End Time\",\"Duration\",\"Activation Method\",\"Activated By\",\"Ended By\",\"End Reason\"");

                    foreach (var s in sessions)
                    {
                        string id = s.Id;
                        string sDate = s.FormattedStartDate;
                        string sTime = s.FormattedStartTime;
                        string eDate = s.FormattedEndDate;
                        string eTime = s.FormattedEndTime;
                        string dur = s.FormattedDuration;
                        string method = s.ActivationMethod;
                        string actBy = s.ActivatedBy;
                        string endBy = string.IsNullOrEmpty(s.EndedBy) ? "System" : s.EndedBy;
                        string reason = s.EndReason;

                        writer.WriteLine($"\"{id}\",\"{sDate}\",\"{sTime}\",\"{eDate}\",\"{eTime}\",\"{dur}\",\"{method}\",\"{actBy}\",\"{endBy}\",\"{reason}\"");
                    }

                    MessageBox.Show($"AFK Log exported successfully to:\n{dlg.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export AFK Log: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
