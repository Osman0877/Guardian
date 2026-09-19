using System.Windows;
using Guardian.Core.Models;

namespace GuardianWpf.Views
{
    public partial class AfkSessionDetailDialog : Window
    {
        public AfkSessionDetailDialog(AfkSessionRecord session)
        {
            InitializeComponent();
            Populate(session);
        }

        private void Populate(AfkSessionRecord s)
        {
            SessionIdSubtext.Text = $"Session ID: {s.Id}";
            StartedValueText.Text = $"{s.FormattedStartDate} at {s.FormattedStartTime}";
            EndedValueText.Text = s.EndTimestamp.HasValue ? $"{s.FormattedEndDate} at {s.FormattedEndTime}" : "Active (In Progress)";

            DurationValueText.Text = s.FormattedDuration;
            ExcludedValueText.Text = s.FormattedDuration;

            MethodValueText.Text = s.ActivationMethod;
            ActivatedByValueText.Text = s.ActivatedBy;
            EndedByValueText.Text = string.IsNullOrEmpty(s.EndedBy) || s.EndedBy == "Pending" ? (s.EndTimestamp.HasValue ? "System" : "Pending") : s.EndedBy;
            ReasonValueText.Text = s.EndReason;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
