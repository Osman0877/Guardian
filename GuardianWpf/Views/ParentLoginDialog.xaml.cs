using System.Windows;
using System.Windows.Input;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public partial class ParentLoginDialog : Window
    {
        private readonly IAuthenticationService _auth;

        public ParentLoginDialog(IAuthenticationService auth)
        {
            InitializeComponent();
            _auth = auth;

            DefaultPasswordHintBorder.Visibility = _auth.IsDefaultPassword ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (s, e) => PasswordInput.Focus();
        }

        private void Unlock_Click(object sender, RoutedEventArgs e)
        {
            PerformUnlock();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformUnlock();
            }
        }

        private void PerformUnlock()
        {
            string pwd = PasswordInput.Password;
            if (_auth.AuthenticateAndStartSession(pwd))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ErrorMessageText.Text = "Incorrect password. Please try again.";
                ErrorMessageText.Visibility = Visibility.Visible;
                PasswordInput.SelectAll();
                PasswordInput.Focus();
            }
        }
    }
}
