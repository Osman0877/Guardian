using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public class RequestItemViewModel
    {
        public ChildRequest Request { get; }
        public string Title => Request.Title;
        public string Reason => Request.Reason;
        public string FormattedDate => $"Submitted: {Request.CreatedAt:MMM dd, yyyy HH:mm}";

        public Visibility PendingVisibility => Request.Status == RequestStatus.Pending ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ResolvedVisibility => Request.Status != RequestStatus.Pending ? Visibility.Visible : Visibility.Collapsed;

        public string StatusText => Request.Status == RequestStatus.Approved ? "APPROVED" : "DENIED";
        public Brush StatusBadgeBackground => Request.Status == RequestStatus.Approved ? new SolidColorBrush(Color.FromRgb(22, 56, 40)) : new SolidColorBrush(Color.FromRgb(56, 20, 30));
        public Brush StatusBadgeForeground => Request.Status == RequestStatus.Approved ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(255, 61, 113));

        public RequestItemViewModel(ChildRequest req)
        {
            Request = req;
        }
    }

    public partial class RequestsView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;
        private readonly IAuthenticationService? _auth;

        public RequestsView(IDatabaseService db, IUsageTrackingService usage, IAuthenticationService? auth = null)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;
            _auth = auth;
            LoadRequests();
        }

        private bool EnsureParentAuth()
        {
            if (_auth == null) return true;
            if (_auth.HasAuthenticatedSession) return true;

            var parentWin = Window.GetWindow(this);
            var dlg = new ParentLoginDialog(_auth) { Owner = parentWin };
            return dlg.ShowDialog() == true;
        }

        public void LoadRequests()
        {
            var reqs = _db.GetRequests();
            var items = reqs.Select(r => new RequestItemViewModel(r)).ToList();

            RequestsItemsControl.ItemsSource = items;
            EmptyStateBorder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Approve_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RequestItemViewModel item)
            {
                if (!EnsureParentAuth()) return;

                item.Request.Status = RequestStatus.Approved;
                item.Request.RespondedAt = DateTime.Now;
                _db.SaveRequest(item.Request);

                // Grant time
                int mins = item.Request.RequestedMinutes > 0 ? item.Request.RequestedMinutes : 30;
                _usage.GrantBonusMinutes(mins);

                _db.LogIncident("REQUEST_APPROVED", "Parent", $"Parent approved request '{item.Title}' (+{mins}m granted).");
                LoadRequests();

                MessageBox.Show($"Request approved! +{mins} minutes has been added to today's screen time.", "Request Approved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Deny_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RequestItemViewModel item)
            {
                if (!EnsureParentAuth()) return;

                item.Request.Status = RequestStatus.Denied;
                item.Request.RespondedAt = DateTime.Now;
                _db.SaveRequest(item.Request);

                _db.LogIncident("REQUEST_DENIED", "Parent", $"Parent denied request '{item.Title}'.");
                LoadRequests();
            }
        }
    }
}
