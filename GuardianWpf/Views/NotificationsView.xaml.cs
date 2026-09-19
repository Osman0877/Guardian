using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public class NotificationItemViewModel
    {
        public GuardianNotification Notification { get; }
        public string Title => Notification.Title;
        public string Message => Notification.Message;
        public string FormattedTimestamp => Notification.Timestamp.ToString("MMM dd HH:mm");
        public Visibility UnreadVisibility => Notification.IsRead ? Visibility.Collapsed : Visibility.Visible;

        public string IconString => Notification.Severity switch
        {
            NotificationSeverity.Alert => "🔴",
            NotificationSeverity.Warning => "🟡",
            NotificationSeverity.Success => "🟢",
            _ => "ℹ️"
        };

        public NotificationItemViewModel(GuardianNotification n)
        {
            Notification = n;
        }
    }

    public partial class NotificationsView : UserControl
    {
        private readonly IDatabaseService _db;

        public NotificationsView(IDatabaseService db)
        {
            InitializeComponent();
            _db = db;
            LoadNotifications();
        }

        public void LoadNotifications()
        {
            var notifs = _db.GetNotifications(40);
            var items = notifs.Select(n => new NotificationItemViewModel(n)).ToList();
            NotificationsListView.ItemsSource = items;
        }

        private void MarkAllRead_Click(object sender, RoutedEventArgs e)
        {
            _db.MarkAllNotificationsRead();
            LoadNotifications();
        }
    }
}
