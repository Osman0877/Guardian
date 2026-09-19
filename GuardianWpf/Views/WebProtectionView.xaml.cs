using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class WebRuleViewModel
    {
        public WebFilterRule Rule { get; }
        public string Domain => Rule.Domain;
        public string Category => Rule.Category;
        public string Reason => Rule.Reason;

        public string StatusText => Rule.IsBlocked ? "🚫 BLOCKED" : "✅ ALLOWED";
        public Brush BadgeBackground => Rule.IsBlocked ? new SolidColorBrush(Color.FromRgb(56, 20, 30)) : new SolidColorBrush(Color.FromRgb(22, 56, 40));
        public Brush BadgeForeground => Rule.IsBlocked ? new SolidColorBrush(Color.FromRgb(255, 61, 113)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));

        public WebRuleViewModel(WebFilterRule rule)
        {
            Rule = rule;
        }
    }

    public partial class WebProtectionView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IWebProtectionService _web;

        public WebProtectionView(IDatabaseService db, IWebProtectionService web)
        {
            _db = db;
            _web = web;
            InitializeComponent();

            LoadRules();
        }

        public void LoadRules()
        {
            var rules = _db.GetWebRules();
            var items = rules.Select(r => new WebRuleViewModel(r)).ToList();

            WebRulesList.ItemsSource = items;
            DomainCountText.Text = $"{items.Count(i => i.Rule.IsBlocked)} Blocked Sites";

            if (_web.IsFilteringActive)
            {
                FilterStatusText.Text = "Active & Enforcing";
                FilterStatusText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
                MasterToggleButton.Content = "🛡️ Protection: ACTIVE";
                MasterToggleButton.Style = ThemeHelper.GetStyle("SuccessButtonStyle");
            }
            else
            {
                FilterStatusText.Text = "Disabled by Parent";
                FilterStatusText.Foreground = ThemeHelper.GetBrush("WarningYellowBrush", Brushes.Gold);
                MasterToggleButton.Content = "⚠️ Protection: DISABLED";
                MasterToggleButton.Style = ThemeHelper.GetStyle("OutlineButtonStyle");
            }
        }

        private void MasterToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_web.IsFilteringActive)
            {
                _web.DisableWebProtection();
            }
            else
            {
                _web.EnableWebProtection();
            }
            LoadRules();
        }

        private void AddDomain_Click(object sender, RoutedEventArgs e)
        {
            string dom = DomainInput.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(dom) || !dom.Contains("."))
            {
                MessageBox.Show("Please enter a valid website domain (e.g. tiktok.com).", "Invalid Domain", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove http/https prefix if pasted
            dom = dom.Replace("https://", "").Replace("http://", "").TrimEnd('/');

            string cat = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom Block";

            var rule = new WebFilterRule
            {
                Domain = dom,
                Category = cat,
                IsBlocked = true,
                Reason = "Parental Block"
            };

            _db.SaveWebRule(rule);
            _web.SyncWebRules();

            DomainInput.Text = "";
            LoadRules();

            _db.LogIncident("WEB_RULE_ADDED", dom, $"Parent added blocked website rule for {dom} ({cat}).");
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WebRuleViewModel item)
            {
                _db.DeleteWebRule(item.Domain);
                _web.SyncWebRules();
                LoadRules();

                _db.LogIncident("WEB_RULE_DELETED", item.Domain, $"Parent removed web rule for {item.Domain}.");
            }
        }
    }
}
