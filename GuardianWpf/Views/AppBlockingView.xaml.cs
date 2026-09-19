using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class AppBlockItemViewModel : INotifyPropertyChanged
    {
        public AppRule Rule { get; }
        private readonly long _usedSeconds;

        public string ProcessName => Rule.ProcessName;
        public string DisplayName => Rule.DisplayName;
        public string Category => Rule.Category;

        public bool IsBlocked
        {
            get => Rule.IsBlocked;
            set
            {
                Rule.IsBlocked = value;
                OnPropertyChanged(nameof(IsBlocked));
                OnPropertyChanged(nameof(BlockButtonText));
                OnPropertyChanged(nameof(BlockButtonStyle));
                OnPropertyChanged(nameof(UsageStatusText));
            }
        }

        public int DailyLimitMinutes
        {
            get => Rule.DailyLimitMinutes;
            set
            {
                Rule.DailyLimitMinutes = value;
                OnPropertyChanged(nameof(DailyLimitMinutes));
                OnPropertyChanged(nameof(LimitDisplayText));
            }
        }

        public string LimitDisplayText => DailyLimitMinutes == 0 ? "Unlimited" : $"{DailyLimitMinutes / 60}h {DailyLimitMinutes % 60:D2}m limit";
        public string UsageStatusText
        {
            get
            {
                if (IsBlocked) return "🚫 Blocked from running";
                TimeSpan ts = TimeSpan.FromSeconds(_usedSeconds);
                return $"Used Today: {(int)ts.TotalHours}h {ts.Minutes:D2}m";
            }
        }

        public string BlockButtonText => IsBlocked ? "🚫 Blocked" : "✅ Allowed";
        public Style? BlockButtonStyle => ThemeHelper.GetStyle(IsBlocked ? "DangerButtonStyle" : "SuccessButtonStyle");

        public AppBlockItemViewModel(AppRule rule, long usedSeconds)
        {
            Rule = rule;
            _usedSeconds = usedSeconds;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class AppBlockingView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IEnforcementService _enforcement;
        private readonly IUsageTrackingService _usage;
        private readonly IAuthenticationService? _auth;
        private List<AppBlockItemViewModel> _allItems = new();

        public AppBlockingView(IDatabaseService db, IEnforcementService enforcement, IUsageTrackingService usage, IAuthenticationService? auth = null)
        {
            _db = db;
            _enforcement = enforcement;
            _usage = usage;
            _auth = auth;
            InitializeComponent();

            LoadApps();
        }

        private bool EnsureParentAuth()
        {
            if (_auth == null) return true;
            if (_auth.HasAuthenticatedSession) return true;

            var parentWin = Window.GetWindow(this);
            var dlg = new ParentLoginDialog(_auth) { Owner = parentWin };
            return dlg.ShowDialog() == true;
        }

        public void LoadApps()
        {
            var rules = _db.GetAllAppRules();
            var dailyUsage = _usage.CurrentDailyRecord;

            _allItems = rules.Select(r =>
            {
                dailyUsage.AppUsages.TryGetValue(r.ProcessName, out var usageEntry);
                return new AppBlockItemViewModel(r, usageEntry?.SecondsUsed ?? 0);
            }).ToList();

            FilterList();
        }

        private void FilterList()
        {
            string q = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            var filtered = string.IsNullOrEmpty(q)
                ? _allItems
                : _allItems.Where(a => a.DisplayName.ToLowerInvariant().Contains(q) || a.ProcessName.ToLowerInvariant().Contains(q)).ToList();

            AppsItemsControl.ItemsSource = filtered;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterList();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && slider.DataContext is AppBlockItemViewModel item)
            {
                if (!EnsureParentAuth())
                {
                    LoadApps();
                    return;
                }
                _db.SaveAppRule(item.Rule);
                _enforcement.RefreshRules();
            }
        }

        private void ToggleBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppBlockItemViewModel item)
            {
                if (!EnsureParentAuth()) return;

                item.IsBlocked = !item.IsBlocked;
                _db.SaveAppRule(item.Rule);
                _enforcement.RefreshRules();

                string verb = item.IsBlocked ? "Blocked" : "Allowed";
                _db.LogIncident($"APP_{verb.ToUpper()}", item.ProcessName, $"{item.DisplayName} was set to {verb} by parent.");
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppBlockItemViewModel item)
            {
                if (!EnsureParentAuth()) return;

                _db.DeleteAppRule(item.ProcessName);
                _allItems.Remove(item);
                FilterList();
                _enforcement.RefreshRules();
            }
        }

        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureParentAuth()) return;

            string pName = ProcessInput.Text.Trim();
            string dName = NameInput.Text.Trim();

            if (string.IsNullOrEmpty(pName))
            {
                MessageBox.Show("Please enter a process executable name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(dName)) dName = pName;

            var newRule = new AppRule
            {
                ProcessName = pName.ToLowerInvariant(),
                DisplayName = dName,
                IsBlocked = false,
                DailyLimitMinutes = 60,
                Category = "Custom"
            };

            _db.SaveAppRule(newRule);
            _enforcement.RefreshRules();

            ProcessInput.Text = "";
            NameInput.Text = "";

            LoadApps();
        }
    }
}
