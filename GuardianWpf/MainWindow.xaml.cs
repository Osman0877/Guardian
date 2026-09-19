using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;
using GuardianWpf.Views;

namespace GuardianWpf
{
    public partial class MainWindow : Window
    {
        private readonly IDatabaseService _db;
        private readonly IAuthenticationService _auth;
        private readonly IUsageTrackingService _usage;
        private readonly IEnforcementService _enforcement;
        private readonly IWebProtectionService _web;

        private GuardianSettings _settings;
        private UserRole _currentRole = UserRole.Parent;

        // 13 Full Parent Views + Child HUD
        private ParentDashboardView? _parentDashboardView;
        private ScreenTimeView? _screenTimeView;
        private AppBlockingView? _appBlockingView;
        private ApplicationsView? _applicationsView;
        private WebProtectionView? _webProtectionView;
        private SchedulesView? _schedulesView;
        private AfkLogView? _afkLogView;
        private RequestsView? _requestsView;
        private ActivityView? _activityView;
        private ReportsView? _reportsView;
        private NotificationsView? _notificationsView;
        private ProtectionView? _protectionView;
        private ChildrenView? _childrenView;
        private SettingsView? _settingsView;
        private ChildHudView? _childHudView;

        public MainWindow(IDatabaseService db, IAuthenticationService auth, IUsageTrackingService usage, IEnforcementService enforcement, IWebProtectionService web)
        {
            InitializeComponent();
            _db = db;
            _auth = auth;
            _usage = usage;
            _enforcement = enforcement;
            _web = web;

            _settings = _db.LoadSettings();
            _currentRole = _settings.ActiveRole == UserRole.NotSelected ? UserRole.Parent : _settings.ActiveRole;

            InitializeViews();
            ApplyCurrentRole();

            Closing += MainWindow_Closing;
        }

        private void InitializeViews()
        {
            _parentDashboardView = new ParentDashboardView(_usage, _enforcement);
            _parentDashboardView.OnInstantLockRequested += () => (Application.Current as App)?.TriggerDeviceLock();

            _screenTimeView = new ScreenTimeView(_db, _usage, _auth);
            _screenTimeView.OnInstantLockRequested += () => (Application.Current as App)?.TriggerDeviceLock();

            _appBlockingView = new AppBlockingView(_db, _enforcement, _usage, _auth);
            _applicationsView = new ApplicationsView(_db, _enforcement);
            _webProtectionView = new WebProtectionView(_db, _web);
            _schedulesView = new SchedulesView(_db, _usage);
            _afkLogView = new AfkLogView(_db, _usage);
            _requestsView = new RequestsView(_db, _usage, _auth);
            _activityView = new ActivityView(_db);
            _reportsView = new ReportsView(_db, _usage);
            _notificationsView = new NotificationsView(_db);
            _protectionView = new ProtectionView(_enforcement);
            _childrenView = new ChildrenView(_db, _usage);
            _settingsView = new SettingsView(_db, _auth, _usage);
            _childHudView = new ChildHudView(_db, _usage);
        }

        private void ApplyCurrentRole()
        {
            var activeChild = _db.GetChild(_usage.ActiveChildId) ?? new ChildProfile { Name = "Alex", Avatar = "👦" };
            SidebarActiveChildText.Text = $"Profile: {activeChild.Avatar} {activeChild.Name}";

            if (_currentRole == UserRole.Parent)
            {
                ActiveRoleBadgeText.Text = "PARENT MODE";
                ActiveRoleBadgeText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.LimeGreen);
                SwitchModeButton.Content = "👦 Switch to Child Mode";

                ParentNavPanel.Visibility = Visibility.Visible;
                ChildNavPanel.Visibility = Visibility.Collapsed;
                GlobalSearchBox.Visibility = Visibility.Visible;

                NavDashboard.IsChecked = true;
                MainContentHost.Content = _parentDashboardView;
            }
            else
            {
                ActiveRoleBadgeText.Text = "CHILD MODE";
                ActiveRoleBadgeText.Foreground = ThemeHelper.GetBrush("AccentGoldBrush", Brushes.Gold);
                SwitchModeButton.Content = "👨 Switch to Parent Mode";

                ParentNavPanel.Visibility = Visibility.Collapsed;
                ChildNavPanel.Visibility = Visibility.Visible;
                GlobalSearchBox.Visibility = Visibility.Collapsed;

                NavChildHome.IsChecked = true;
                MainContentHost.Content = _childHudView;
            }

            _settings.ActiveRole = _currentRole;
            _db.SaveSettings(_settings);
        }

        private void SwitchMode_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRole == UserRole.Child)
            {
                var loginDlg = new ParentLoginDialog(_auth) { Owner = this };
                if (loginDlg.ShowDialog() == true)
                {
                    _currentRole = UserRole.Parent;
                    ApplyCurrentRole();
                }
            }
            else
            {
                _currentRole = UserRole.Child;
                _auth.EndSession();
                ApplyCurrentRole();
            }
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender == NavDashboard) MainContentHost.Content = _parentDashboardView;
            else if (sender == NavScreenTime) { _screenTimeView?.UpdateUI(); MainContentHost.Content = _screenTimeView; }
            else if (sender == NavAppBlocking) { _appBlockingView?.LoadApps(); MainContentHost.Content = _appBlockingView; }
            else if (sender == NavApplications) MainContentHost.Content = _applicationsView;
            else if (sender == NavWebProtection) { _webProtectionView?.LoadRules(); MainContentHost.Content = _webProtectionView; }
            else if (sender == NavSchedules) MainContentHost.Content = _schedulesView;
            else if (sender == NavAfkLog) { _afkLogView?.LoadData(); MainContentHost.Content = _afkLogView; }
            else if (sender == NavRequests) { _requestsView?.LoadRequests(); MainContentHost.Content = _requestsView; }
            else if (sender == NavActivity) { _activityView?.LoadActivity(); MainContentHost.Content = _activityView; }
            else if (sender == NavReports) { _reportsView?.LoadLogs(); MainContentHost.Content = _reportsView; }
            else if (sender == NavNotifications) { _notificationsView?.LoadNotifications(); MainContentHost.Content = _notificationsView; }
            else if (sender == NavProtection) MainContentHost.Content = _protectionView;
            else if (sender == NavChildren) { _childrenView?.LoadChildren(); MainContentHost.Content = _childrenView; }
            else if (sender == NavSettings) MainContentHost.Content = _settingsView;
        }

        private void ChildNav_Click(object sender, RoutedEventArgs e)
        {
            if (sender == NavChildHome)
            {
                MainContentHost.Content = _childHudView;
            }
            else if (sender == NavChildScreenTime)
            {
                _screenTimeView?.UpdateUI();
                MainContentHost.Content = _screenTimeView;
            }
            else if (sender == NavChildApps)
            {
                _appBlockingView?.LoadApps();
                MainContentHost.Content = _appBlockingView;
            }
            else if (sender == NavChildRequests)
            {
                _requestsView?.LoadRequests();
                MainContentHost.Content = _requestsView;
            }
            else if (sender == NavChildNotifications)
            {
                _notificationsView?.LoadNotifications();
                MainContentHost.Content = _notificationsView;
            }
        }

        private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = GlobalSearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(q)) return;

            // Direct search match routing
            if (q.Contains("app") || q.Contains("block") || q.Contains("roblox") || q.Contains("game"))
            {
                NavAppBlocking.IsChecked = true;
                _appBlockingView?.LoadApps();
                MainContentHost.Content = _appBlockingView;
            }
            else if (q.Contains("web") || q.Contains("site") || q.Contains("tiktok") || q.Contains("filter"))
            {
                NavWebProtection.IsChecked = true;
                _webProtectionView?.LoadRules();
                MainContentHost.Content = _webProtectionView;
            }
            else if (q.Contains("time") || q.Contains("limit") || q.Contains("hour") || q.Contains("bedtime"))
            {
                NavScreenTime.IsChecked = true;
                _screenTimeView?.UpdateUI();
                MainContentHost.Content = _screenTimeView;
            }
            else if (q.Contains("sched") || q.Contains("day") || q.Contains("wednesday"))
            {
                NavSchedules.IsChecked = true;
                MainContentHost.Content = _schedulesView;
            }
            else if (q.Contains("child") || q.Contains("profile") || q.Contains("alex") || q.Contains("emma"))
            {
                NavChildren.IsChecked = true;
                _childrenView?.LoadChildren();
                MainContentHost.Content = _childrenView;
            }
            else if (q.Contains("req") || q.Contains("ask"))
            {
                NavRequests.IsChecked = true;
                _requestsView?.LoadRequests();
                MainContentHost.Content = _requestsView;
            }
            else if (q.Contains("pass") || q.Contains("set") || q.Contains("admin") || q.Contains("1111"))
            {
                NavSettings.IsChecked = true;
                MainContentHost.Content = _settingsView;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Always minimize/hide to tray when window is closed so background time tracking and process enforcement remain active
            e.Cancel = true;
            Hide();
        }
    }
}