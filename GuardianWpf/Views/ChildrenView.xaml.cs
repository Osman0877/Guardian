using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public class ChildProfileViewModel
    {
        public ChildProfile Profile { get; }
        public string Name => Profile.Name;
        public string Avatar => Profile.Avatar;
        public string AgeString => $"Age {Profile.Age}";
        public string LimitString => $"Daily Limit: {Profile.DailyLimitMinutes / 60}h {Profile.DailyLimitMinutes % 60:D2}m ({Profile.DailyLimitMinutes} mins)";
        public string BedtimeString => Profile.BedtimeEnabled ? $"Bedtime: {Profile.Bedtime:hh\\:mm} PM" : "Bedtime: Disabled";
        public Visibility ActiveBadgeVisibility => Profile.IsActive ? Visibility.Visible : Visibility.Collapsed;

        public ChildProfileViewModel(ChildProfile profile)
        {
            Profile = profile;
        }
    }

    public partial class ChildrenView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;

        public ChildrenView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;
            LoadChildren();
        }

        public void LoadChildren()
        {
            var children = _db.GetAllChildren();
            var activeId = _usage.ActiveChildId;

            foreach (var c in children)
            {
                c.IsActive = (c.Id == activeId);
            }

            var items = children.Select(c => new ChildProfileViewModel(c)).ToList();
            ChildrenItemsControl.ItemsSource = items;
        }

        private void SetActive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ChildProfileViewModel item)
            {
                _usage.SetActiveChild(item.Profile.Id);
                LoadChildren();

                _db.LogIncident("ACTIVE_CHILD_SWITCHED", "Profile", $"Active device child switched to {item.Profile.Name}.");
                MessageBox.Show($"Active child user switched to {item.Profile.Name}!", "Child Profile Active", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ChildProfileViewModel item)
            {
                if (item.Profile.Id == "default")
                {
                    MessageBox.Show("Cannot delete the default profile.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _db.DeleteChild(item.Profile.Id);
                LoadChildren();
            }
        }

        private void AddChild_Click(object sender, RoutedEventArgs e)
        {
            string name = ChildNameInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a valid child name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string avatar = (AvatarCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "👦";
            int age = int.TryParse(AgeInput.Text, out int a) ? a : 10;

            var newChild = new ChildProfile
            {
                Name = name,
                Avatar = avatar,
                Age = age,
                DailyLimitMinutes = 120,
                Bedtime = new TimeSpan(21, 0, 0),
                BedtimeEnabled = true
            };

            _db.SaveChild(newChild);

            ChildNameInput.Text = "";
            LoadChildren();
        }
    }
}
