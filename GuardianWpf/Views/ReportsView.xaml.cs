using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public class IncidentItemViewModel
    {
        public string FormattedTime { get; set; } = "";
        public string EventType { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public partial class ReportsView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService? _usage;

        public ReportsView(IDatabaseService db, IUsageTrackingService? usage = null)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;
            LoadLogs();
        }

        public void LoadLogs()
        {
            var incidents = _db.GetIncidents(50);
            var items = incidents.Select(i => new IncidentItemViewModel
            {
                FormattedTime = i.Timestamp.ToString("MMM dd HH:mm:ss"),
                EventType = FormatEventType(i.EventType),
                ProcessName = i.ProcessName,
                Description = i.Description
            }).ToList();

            IncidentsList.ItemsSource = items;

            // Load Today's Usage Breakdown (Screen Time vs AFK Time vs Total Device-On Time)
            var today = _usage?.CurrentDailyRecord ?? _db.GetOrCreateDailyUsage(DateTime.Today, _usage?.ActiveChildId ?? "default");
            if (today != null)
            {
                TimeSpan screenTs = TimeSpan.FromSeconds(today.TotalDeviceSecondsUsed);
                TimeSpan afkTs = TimeSpan.FromSeconds(today.TotalAfkSecondsUsed);
                TimeSpan totalTs = TimeSpan.FromSeconds(today.TotalDeviceSecondsUsed + today.TotalAfkSecondsUsed);

                ReportScreenTimeText.Text = $"{(int)screenTs.TotalHours}h {screenTs.Minutes:D2}m";
                ReportAfkTimeText.Text = $"{(int)afkTs.TotalHours}h {afkTs.Minutes:D2}m";
                ReportTotalDeviceTimeText.Text = $"{(int)totalTs.TotalHours}h {totalTs.Minutes:D2}m";
            }
        }

        private static string FormatEventType(string raw)
        {
            return raw switch
            {
                "DEVICE_LIMIT_REACHED" => "🔒 Device Locked",
                "BLOCKED_APP_TERMINATED" => "🚫 Blocked App Closed",
                "APP_LIMIT_EXCEEDED" => "⏰ App Limit Reached",
                "PARENT_UNLOCK_GRANTED" => "🔑 Parent Unlock",
                "CHILD_REQUEST_SUBMITTED" => "📩 Request Submitted",
                "REQUEST_APPROVED" => "✅ Request Approved",
                "REQUEST_DENIED" => "❌ Request Denied",
                "AFK_ACTIVATED" => "🟢 AFK Activated",
                "AFK_DEACTIVATED" => "⚪ AFK Deactivated",
                "AFK_EXPIRED" => "⏰ AFK Expired",
                _ => raw
            };
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }
    }
}
