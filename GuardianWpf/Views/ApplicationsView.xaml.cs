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
    public class AppRuleItem : INotifyPropertyChanged
    {
        public AppRule Rule { get; }
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
            }
        }

        public int DailyLimitMinutes
        {
            get => Rule.DailyLimitMinutes;
            set
            {
                Rule.DailyLimitMinutes = value;
                OnPropertyChanged(nameof(DailyLimitMinutes));
                OnPropertyChanged(nameof(LimitDisplayString));
            }
        }

        public string LimitDisplayString => DailyLimitMinutes == 0 ? "Unlimited" : $"{DailyLimitMinutes / 60}h {DailyLimitMinutes % 60:D2}m ({DailyLimitMinutes} mins)";
        public string BlockButtonText => IsBlocked ? "🚫 Blocked" : "✅ Allowed";
        public Style? BlockButtonStyle => ThemeHelper.GetStyle(IsBlocked ? "DangerButtonStyle" : "SuccessButtonStyle");

        public AppRuleItem(AppRule rule)
        {
            Rule = rule;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class ApplicationsView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IEnforcementService _enforcement;
        private List<AppRuleItem> _allRules = new();

        public ApplicationsView(IDatabaseService db, IEnforcementService enforcement)
        {
            _db = db;
            _enforcement = enforcement;
            InitializeComponent();

            LoadRules();
        }

        private void LoadRules()
        {
            var rules = _db.GetAllAppRules();
            _allRules = rules.Select(r => new AppRuleItem(r)).ToList();
            FilterList();
        }

        private void FilterList()
        {
            string q = SearchInput.Text?.Trim().ToLowerInvariant() ?? "";
            var filtered = string.IsNullOrEmpty(q)
                ? _allRules
                : _allRules.Where(r => r.DisplayName.ToLowerInvariant().Contains(q) || r.ProcessName.ToLowerInvariant().Contains(q)).ToList();

            AppRulesItemsControl.ItemsSource = filtered;
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterList();
        }

        private void LimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && slider.DataContext is AppRuleItem item)
            {
                _db.SaveAppRule(item.Rule);
                _enforcement.RefreshRules();
            }
        }

        private void ToggleBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppRuleItem item)
            {
                item.IsBlocked = !item.IsBlocked;
                _db.SaveAppRule(item.Rule);
                _enforcement.RefreshRules();
            }
        }

        private void DeleteApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppRuleItem item)
            {
                _db.DeleteAppRule(item.ProcessName);
                _allRules.Remove(item);
                FilterList();
                _enforcement.RefreshRules();
            }
        }

        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            string pName = NewProcessNameInput.Text.Trim();
            string dName = NewDisplayNameInput.Text.Trim();

            if (string.IsNullOrEmpty(pName))
            {
                MessageBox.Show("Please enter a valid process name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            NewProcessNameInput.Text = "";
            NewDisplayNameInput.Text = "";

            LoadRules();
        }
    }
}
