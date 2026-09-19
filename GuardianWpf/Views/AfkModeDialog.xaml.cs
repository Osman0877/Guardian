using System.Windows;
using System.Windows.Input;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public partial class AfkModeDialog : Window
    {
        private readonly IUsageTrackingService _usage;
        private readonly bool _isDeactivating;

        public int SelectedDurationMinutes { get; private set; } = 15;

        public AfkModeDialog(IUsageTrackingService usage, bool isDeactivating = false)
        {
            InitializeComponent();
            _usage = usage;
            _isDeactivating = isDeactivating;

            if (_isDeactivating)
            {
                DialogHeaderIcon.Text = "🛑";
                DialogTitleText.Text = "Deactivate AFK Mode";
                DurationSelectionPanel.Visibility = Visibility.Collapsed;
                ConfirmActionButton.Content = "🛑 Deactivate AFK";
                ConfirmActionButton.Style = (Style)FindResource("DangerButtonStyle");
                Height = 330;
            }

            Loaded += (s, e) => PasswordInput.Focus();
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformAction();
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            PerformAction();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PerformAction()
        {
            string password = PasswordInput.Password;

            if (_isDeactivating)
            {
                if (_usage.DeactivateAfkMode(password))
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ErrorMessageText.Text = "Incorrect parent password. Please try again.";
                    ErrorMessageText.Visibility = Visibility.Visible;
                    PasswordInput.SelectAll();
                    PasswordInput.Focus();
                }
            }
            else
            {
                int duration = 0;
                if (Radio15m.IsChecked == true) duration = 15;
                else if (Radio30m.IsChecked == true) duration = 30;
                else if (Radio1h.IsChecked == true) duration = 60;
                else if (Radio2h.IsChecked == true) duration = 120;
                else if (RadioManual.IsChecked == true) duration = 0; // Until manually disabled

                SelectedDurationMinutes = duration;

                if (_usage.ActivateAfkMode(password, duration))
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ErrorMessageText.Text = "Incorrect parent password. Please try again.";
                    ErrorMessageText.Visibility = Visibility.Visible;
                    PasswordInput.SelectAll();
                    PasswordInput.Focus();
                }
            }
        }
    }
}
