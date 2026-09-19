using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public partial class ProtectionView : UserControl
    {
        private readonly IEnforcementService _enforcement;

        public ProtectionView(IEnforcementService enforcement)
        {
            InitializeComponent();
            _enforcement = enforcement;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_enforcement.IsWatchdogRunning)
            {
                WatchdogStatusText.Text = "Running (GuardianWatchdog.exe)";
                WatchdogStatusText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
            }
            else
            {
                WatchdogStatusText.Text = "Stopped / Recovering...";
                WatchdogStatusText.Foreground = ThemeHelper.GetBrush("WarningYellowBrush", Brushes.Gold);
            }
        }

        private void TestKillWatchdog_Click(object sender, RoutedEventArgs e)
        {
            _enforcement.KillWatchdogForTesting();
            UpdateStatus();
            MessageBox.Show("GuardianWatchdog process was terminated. Guardian will detect its absence and restart it automatically.", "Watchdog Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RestartWatchdog_Click(object sender, RoutedEventArgs e)
        {
            _enforcement.EnsureWatchdogRunning();
            UpdateStatus();
            MessageBox.Show("GuardianWatchdog service started.", "Watchdog Restarted", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
