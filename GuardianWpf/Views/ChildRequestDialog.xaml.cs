using System.Windows;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public partial class ChildRequestDialog : Window
    {
        private readonly IDatabaseService _db;

        public ChildRequestDialog(IDatabaseService db)
        {
            InitializeComponent();
            _db = db;
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            int selectedIdx = RequestTypeCombo.SelectedIndex;
            var type = (selectedIdx <= 1) ? RequestType.ExtraScreenTime : (selectedIdx == 2 ? RequestType.UnlockApp : RequestType.GeneralRequest);
            int mins = (selectedIdx == 0) ? 30 : (selectedIdx == 1 ? 60 : 0);
            string title = (RequestTypeCombo.SelectedItem as FrameworkElement)?.DataContext?.ToString() ?? "Screen Time Request";

            var req = new ChildRequest
            {
                Type = type,
                Title = selectedIdx switch
                {
                    0 => "Request for +30 Minutes Screen Time",
                    1 => "Request for +1 Hour Screen Time",
                    2 => "Request to Unlock App",
                    _ => "General Permission Request"
                },
                Reason = string.IsNullOrWhiteSpace(ReasonInput.Text) ? "No reason specified." : ReasonInput.Text.Trim(),
                RequestedMinutes = mins,
                Status = RequestStatus.Pending
            };

            _db.SaveRequest(req);
            _db.LogIncident("CHILD_REQUEST_SUBMITTED", "Child", $"Child submitted request: {req.Title}");

            MessageBox.Show("Your request has been submitted! Your parent will review it from their dashboard.", "Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
