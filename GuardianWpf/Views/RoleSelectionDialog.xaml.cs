using System.Windows;
using Guardian.Core.Models;

namespace GuardianWpf.Views
{
    public partial class RoleSelectionDialog : Window
    {
        public UserRole SelectedRole { get; private set; } = UserRole.NotSelected;

        public RoleSelectionDialog()
        {
            InitializeComponent();
        }

        private void Parent_Click(object sender, RoutedEventArgs e)
        {
            SelectedRole = UserRole.Parent;
            DialogResult = true;
            Close();
        }

        private void Child_Click(object sender, RoutedEventArgs e)
        {
            SelectedRole = UserRole.Child;
            DialogResult = true;
            Close();
        }
    }
}
