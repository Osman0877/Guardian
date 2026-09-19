using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public class ActivityEventViewModel
    {
        public string FormattedTimestamp { get; set; } = "";
        public string EventTag { get; set; } = "";
        public string Description { get; set; } = "";
        public Brush BadgeBackground { get; set; } = Brushes.Transparent;
        public Brush BadgeForeground { get; set; } = Brushes.White;
    }

    public partial class ActivityView : UserControl
    {
        private readonly IDatabaseService _db;

        public ActivityView(IDatabaseService db)
        {
            InitializeComponent();
            _db = db;
            LoadActivity();
        }

        public void LoadActivity()
        {
            var incidents = _db.GetIncidents(60);
            var items = incidents.Select(i =>
            {
                (string tag, Brush bg, Brush fg) = ResolveTag(i.EventType);
                return new ActivityEventViewModel
                {
                    FormattedTimestamp = i.Timestamp.ToString("MMM dd HH:mm:ss"),
                    EventTag = tag,
                    Description = i.Description,
                    BadgeBackground = bg,
                    BadgeForeground = fg
                };
            }).ToList();

            ActivityListView.ItemsSource = items;
        }

        private static (string, Brush, Brush) ResolveTag(string raw)
        {
            return raw switch
            {
                "DEVICE_LOCKED" or "DEVICE_LIMIT_REACHED" =>
                    ("🔒 DEVICE LOCK", new SolidColorBrush(Color.FromRgb(56, 20, 30)), new SolidColorBrush(Color.FromRgb(255, 61, 113))),
                "PARENT_UNLOCK_GRANTED" =>
                    ("🔑 PARENT UNLOCK", new SolidColorBrush(Color.FromRgb(22, 56, 40)), new SolidColorBrush(Color.FromRgb(0, 230, 118))),
                "BLOCKED_APP_TERMINATED" =>
                    ("🚫 APP TERMINATED", new SolidColorBrush(Color.FromRgb(56, 20, 30)), new SolidColorBrush(Color.FromRgb(255, 61, 113))),
                "APP_LIMIT_EXCEEDED" =>
                    ("⏰ LIMIT REACHED", new SolidColorBrush(Color.FromRgb(56, 36, 16)), new SolidColorBrush(Color.FromRgb(255, 184, 0))),
                "CHILD_REQUEST_SUBMITTED" =>
                    ("📩 CHILD REQUEST", new SolidColorBrush(Color.FromRgb(16, 40, 60)), new SolidColorBrush(Color.FromRgb(0, 168, 255))),
                "REQUEST_APPROVED" =>
                    ("✅ REQ APPROVED", new SolidColorBrush(Color.FromRgb(22, 56, 40)), new SolidColorBrush(Color.FromRgb(0, 230, 118))),
                "REQUEST_DENIED" =>
                    ("❌ REQ DENIED", new SolidColorBrush(Color.FromRgb(56, 20, 30)), new SolidColorBrush(Color.FromRgb(255, 61, 113))),
                "WEB_PROTECTION_ENABLED" or "WEB_RULE_ADDED" =>
                    ("🌐 WEB FILTER", new SolidColorBrush(Color.FromRgb(16, 40, 60)), new SolidColorBrush(Color.FromRgb(0, 168, 255))),
                "AFK_ACTIVATED" =>
                    ("🟢 AFK ACTIVATED", new SolidColorBrush(Color.FromRgb(22, 56, 40)), new SolidColorBrush(Color.FromRgb(0, 230, 118))),
                "AFK_DEACTIVATED" =>
                    ("⚪ AFK OFF", new SolidColorBrush(Color.FromRgb(30, 42, 64)), Brushes.White),
                "AFK_EXPIRED" =>
                    ("⏰ AFK EXPIRED", new SolidColorBrush(Color.FromRgb(56, 36, 16)), new SolidColorBrush(Color.FromRgb(255, 184, 0))),
                _ => ("📌 SYSTEM EVENT", new SolidColorBrush(Color.FromRgb(30, 42, 64)), Brushes.White)
            };
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadActivity();
        }
    }
}
