using System.Windows;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public partial class ChangePasswordDialog : Window
    {
        private readonly IAuthenticationService _auth;

        public ChangePasswordDialog(IAuthenticationService auth)
        {
            InitializeComponent();
            _auth = auth;
            Loaded += (s, e) => CurrentPasswordInput.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string curr = CurrentPasswordInput.Password;
            string newP = NewPasswordInput.Password;
            string conf = ConfirmPasswordInput.Password;

            if (newP != conf)
            {
                ShowError("New passwords do not match.");
                return;
            }

            if (_auth.SetNewPassword(curr, newP, out string err))
            {
                MessageBox.Show("Parent administrator password successfully updated!", "Guardian Security", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError(err);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
