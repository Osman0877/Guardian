using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardian.Core.Models;
using Guardian.Core.Services;
using GuardianWpf.Styles;

namespace GuardianWpf.Views
{
    public class ChildAppUsageItem
    {
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string UsageAndLimitText { get; set; } = "";
        public double UsagePercent { get; set; } = 0;
        public string StatusBadgeText { get; set; } = "";
        public Brush BadgeBackground { get; set; } = Brushes.Transparent;
        public Brush BadgeForeground { get; set; } = Brushes.White;
        public Brush ProgressBrush { get; set; } = Brushes.DodgerBlue;
    }

    public class ChildInvitationItem
    {
        public string Id { get; set; } = "";
        public string ParentUsernameText { get; set; } = "";
        public string RequestMetaText { get; set; } = "";
    }

    public partial class ChildDashboardView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;

        private bool _isInitializingCombo = true;

        public ChildDashboardView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;

            PopulateChildrenCombo();
            UpdateUI();
            CheckLinkedWebAccount();
            _ = LoadPendingInvitationsAsync();
            _usage.OnUsageUpdated += () => Dispatcher.Invoke(UpdateUI);
        }

        private void CheckLinkedWebAccount()
        {
            var settings = _db.LoadSettings();
            if (!string.IsNullOrWhiteSpace(settings.LinkedWebUsername))
            {
                ChildAccountUsernameBox.Text = settings.LinkedWebUsername;
                ChildAccountStatusBadge.Background = new SolidColorBrush(Color.FromRgb(27, 77, 62));
                ChildAccountBadgeText.Text = $"Linked: {settings.LinkedWebUsername}";
                ChildAccountBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                ChildAccountStatusText.Visibility = Visibility.Visible;
                ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.MediumSeaGreen);
                ChildAccountStatusText.Text = $"✓ Device is connected to account '{settings.LinkedWebUsername}'. Log in at https://guardian-parent-server.onrender.com to manage remotely.";
            }
        }

        private void PopulateChildrenCombo()
        {
            _isInitializingCombo = true;
            var children = _db.GetAllChildren();
            if (children.Count == 0)
            {
                children.Add(new ChildProfile { Id = "default", Name = "Alex" });
            }
            ActiveChildCombo.ItemsSource = children;
            ActiveChildCombo.SelectedValue = _usage.ActiveChildId;
            _isInitializingCombo = false;
        }

        private void ActiveChildCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingCombo || ActiveChildCombo.SelectedValue == null) return;
            string selectedId = ActiveChildCombo.SelectedValue.ToString() ?? "default";
            _usage.SetActiveChild(selectedId);
            UpdateUI();
        }

        public void UpdateUI()
        {
            long usedSecs = _usage.TotalDeviceSecondsToday;
            long remSecs = _usage.RemainingDeviceSecondsToday;
            int limitMins = _usage.DailyLimitMinutesToday;

            TimeSpan usedTs = TimeSpan.FromSeconds(usedSecs);
            TimeSpan remTs = TimeSpan.FromSeconds(remSecs);

            ChildUsedText.Text = $"{(int)usedTs.TotalHours}h {usedTs.Minutes:D2}m";
            ChildRemainingText.Text = $"{(int)remTs.TotalHours}h {remTs.Minutes:D2}m";
            ChildScheduleLabel.Text = $"Daily limit: {limitMins / 60}h {limitMins % 60:D2}m ({DateTime.Today.DayOfWeek})";

            long totalAllowedSecs = (limitMins * 60L) + _usage.CurrentDailyRecord.ManualBonusSeconds;
            if (totalAllowedSecs > 0)
            {
                double pct = Math.Min(100.0, (usedSecs / (double)totalAllowedSecs) * 100.0);
                ChildProgressBar.Value = pct;
                ChildProgressBar.Foreground = pct >= 90
                    ? ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson)
                    : ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            }

            // Load child app rules & usage
            var rules = _db.GetAllAppRules();
            var dailyUsage = _usage.CurrentDailyRecord;

            var items = rules.Select(r =>
            {
                dailyUsage.AppUsages.TryGetValue(r.ProcessName, out var usageEntry);
                long sec = usageEntry?.SecondsUsed ?? 0;
                TimeSpan appTs = TimeSpan.FromSeconds(sec);

                string usageStr = $"{(int)appTs.TotalHours}h {appTs.Minutes:D2}m";
                string limitStr = r.DailyLimitMinutes > 0 ? $" / {r.DailyLimitMinutes / 60}h {r.DailyLimitMinutes % 60:D2}m" : " (Unlimited)";
                double appPct = r.DailyLimitMinutes > 0 ? Math.Min(100.0, (sec / (double)(r.DailyLimitMinutes * 60.0)) * 100.0) : 0;

                string badge = r.IsBlocked ? "BLOCKED" : (r.DailyLimitMinutes > 0 && appPct >= 100 ? "TIME UP" : "ALLOWED");
                Brush bg = r.IsBlocked || (r.DailyLimitMinutes > 0 && appPct >= 100) ? new SolidColorBrush(Color.FromRgb(56, 20, 30)) : new SolidColorBrush(Color.FromRgb(22, 56, 40));
                Brush fg = r.IsBlocked || (r.DailyLimitMinutes > 0 && appPct >= 100) ? new SolidColorBrush(Color.FromRgb(255, 61, 113)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));

                return new ChildAppUsageItem
                {
                    DisplayName = r.DisplayName,
                    Category = r.Category,
                    UsageAndLimitText = usageStr + limitStr,
                    UsagePercent = appPct,
                    StatusBadgeText = badge,
                    BadgeBackground = bg,
                    BadgeForeground = fg,
                    ProgressBrush = appPct >= 90 ? (Brush)FindResource("DangerRedBrush") : (Brush)FindResource("AccentBlueBrush")
                };
            }).ToList();

            ChildAppsList.ItemsSource = items;
        }

        private async void SaveChildAccount_Click(object sender, RoutedEventArgs e)
        {
            string username = ChildAccountUsernameBox.Text.Trim();
            string password = ChildAccountPasswordBox.Password.Trim();
            string confirm = ChildAccountConfirmPasswordBox.Password.Trim();

            ChildAccountStatusText.Visibility = Visibility.Visible;

            if (string.IsNullOrWhiteSpace(username))
            {
                ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                ChildAccountStatusText.Text = "❌ Please enter a username.";
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            {
                ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                ChildAccountStatusText.Text = "❌ Password must be at least 4 characters long.";
                return;
            }

            if (password != confirm)
            {
                ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                ChildAccountStatusText.Text = "❌ Password and Confirm Password do not match.";
                return;
            }

            SaveChildAccountBtn.IsEnabled = false;
            ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("AccentBlueBrush", Brushes.DodgerBlue);
            ChildAccountStatusText.Text = "⏳ Registering web account & connecting device...";

            var settings = _db.LoadSettings();
            string deviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? $"gdev_{Guid.NewGuid():N}"[..17] : settings.DeviceId;

            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var payload = new
                {
                    username,
                    password,
                    deviceId,
                    deviceName = Environment.MachineName
                };

                string[] serverUrls = new[]
                {
                    "https://guardian-parent-server.onrender.com/api/auth/register-device-account",
                    "http://localhost:5000/api/auth/register-device-account"
                };

                bool registered = false;
                string serverMessage = "";

                foreach (var url in serverUrls)
                {
                    try
                    {
                        var response = await client.PostAsJsonAsync(url, payload);
                        var responseContent = await response.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
                        var root = doc.RootElement;
                        bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                        string msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";

                        if (response.IsSuccessStatusCode && success)
                        {
                            registered = true;
                            serverMessage = msg;
                            break;
                        }
                        else
                        {
                            serverMessage = !string.IsNullOrEmpty(msg) ? msg : "Registration failed.";
                            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Try next URL
                    }
                }

                if (registered)
                {
                    settings.LinkedWebUsername = username;
                    _db.SaveSettings(settings);

                    ChildAccountStatusBadge.Background = new SolidColorBrush(Color.FromRgb(27, 77, 62));
                    ChildAccountBadgeText.Text = $"Linked: {username}";
                    ChildAccountBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                    ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.MediumSeaGreen);
                    ChildAccountStatusText.Text = $"✓ {serverMessage} Use username '{username}' and your password to log in at https://guardian-parent-server.onrender.com";
                }
                else
                {
                    ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                    ChildAccountStatusText.Text = $"❌ {(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Unable to reach Guardian Web Server. Check internet connection.")}";
                }
            }
            catch (Exception ex)
            {
                ChildAccountStatusText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                ChildAccountStatusText.Text = $"❌ Connection error: {ex.Message}";
            }
            finally
            {
                SaveChildAccountBtn.IsEnabled = true;
            }
        }

        private void RequestTime_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ChildRequestDialog(_db) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }

        private async System.Threading.Tasks.Task LoadPendingInvitationsAsync()
        {
            var settings = _db.LoadSettings();
            string childUsername = !string.IsNullOrWhiteSpace(settings.LinkedWebUsername)
                ? settings.LinkedWebUsername
                : (ActiveChildCombo.SelectedItem is ChildProfile cp ? cp.Name : "Alex");

            var childUser = _db.GetUserByUsername(childUsername);
            string childUserId = childUser?.Id ?? _usage.ActiveChildId;

            var displayItems = new List<ChildInvitationItem>();

            // Check local DB pending invitations
            var pendingLocal = _db.GetPendingInvitationsForChild(childUserId);
            foreach (var inv in pendingLocal)
            {
                displayItems.Add(new ChildInvitationItem
                {
                    Id = inv.Id,
                    ParentUsernameText = $"👨 Parent Account: {inv.ParentUsername}",
                    RequestMetaText = $"Requested connection on {inv.CreatedAt.ToLocalTime():g} • Status: Pending"
                });
            }

            // Also check remote server if connected
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string url = $"https://guardian-parent-server.onrender.com/api/family/invitations?childUserId={childUserId}";
                var remoteInvs = await client.GetFromJsonAsync<List<ParentChildInvitation>>(url);
                if (remoteInvs != null)
                {
                    foreach (var inv in remoteInvs)
                    {
                        if (inv.Status == InvitationStatus.Pending && !displayItems.Any(d => d.Id == inv.Id))
                        {
                            displayItems.Add(new ChildInvitationItem
                            {
                                Id = inv.Id,
                                ParentUsernameText = $"👨 Parent Account: {inv.ParentUsername}",
                                RequestMetaText = $"Requested connection on {inv.CreatedAt.ToLocalTime():g} • Status: Pending"
                            });
                        }
                    }
                }
            }
            catch { }

            if (displayItems.Count > 0)
            {
                ChildInvitationsList.ItemsSource = displayItems;
                ChildInvitationsList.Visibility = Visibility.Visible;
                NoInvitationsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ChildInvitationsList.ItemsSource = null;
                ChildInvitationsList.Visibility = Visibility.Collapsed;
                NoInvitationsText.Visibility = Visibility.Visible;
            }
        }

        private async void RefreshInvitations_Click(object sender, RoutedEventArgs e)
        {
            await LoadPendingInvitationsAsync();
        }

        private async void AcceptInvitation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string invId)
            {
                btn.IsEnabled = false;
                var settings = _db.LoadSettings();
                string childUsername = !string.IsNullOrWhiteSpace(settings.LinkedWebUsername)
                    ? settings.LinkedWebUsername
                    : (ActiveChildCombo.SelectedItem is ChildProfile cp ? cp.Name : "Alex");

                var childUser = _db.GetUserByUsername(childUsername);
                string childUserId = childUser?.Id ?? _usage.ActiveChildId;

                bool success = false;
                string message = "";

                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    string url = $"https://guardian-parent-server.onrender.com/api/family/invitations/{invId}/accept?childUserId={childUserId}";
                    var res = await client.PostAsync(url, null);
                    var content = await res.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    success = res.IsSuccessStatusCode && doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
                    message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                }
                catch { }

                if (!success)
                {
                    var inv = _db.GetInvitation(invId);
                    if (inv != null && inv.Status == InvitationStatus.Pending)
                    {
                        _db.UpdateInvitationStatus(inv.Id, InvitationStatus.Accepted, DateTime.UtcNow);
                        _db.SaveRelationship(new ParentChildRelationship
                        {
                            ParentUserId = inv.ParentUserId,
                            ChildUserId = inv.ChildUserId,
                            IsActive = true
                        });
                        success = true;
                        message = $"Successfully connected to parent '{inv.ParentUsername}'!";
                    }
                }

                InvitationActionResultText.Visibility = Visibility.Visible;
                if (success)
                {
                    InvitationActionResultText.Foreground = ThemeHelper.GetBrush("SuccessGreenBrush", Brushes.MediumSeaGreen);
                    InvitationActionResultText.Text = $"✓ {(!string.IsNullOrEmpty(message) ? message : "Connected to parent!")}";
                }
                else
                {
                    InvitationActionResultText.Foreground = ThemeHelper.GetBrush("DangerRedBrush", Brushes.Crimson);
                    InvitationActionResultText.Text = $"❌ Failed to accept invitation: {message}";
                }

                await LoadPendingInvitationsAsync();
            }
        }

        private async void DeclineInvitation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string invId)
            {
                btn.IsEnabled = false;
                var settings = _db.LoadSettings();
                string childUsername = !string.IsNullOrWhiteSpace(settings.LinkedWebUsername)
                    ? settings.LinkedWebUsername
                    : (ActiveChildCombo.SelectedItem is ChildProfile cp ? cp.Name : "Alex");

                var childUser = _db.GetUserByUsername(childUsername);
                string childUserId = childUser?.Id ?? _usage.ActiveChildId;

                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    string url = $"https://guardian-parent-server.onrender.com/api/family/invitations/{invId}/decline?childUserId={childUserId}";
                    await client.PostAsync(url, null);
                }
                catch { }

                _db.UpdateInvitationStatus(invId, InvitationStatus.Declined, DateTime.UtcNow);

                InvitationActionResultText.Visibility = Visibility.Visible;
                InvitationActionResultText.Foreground = ThemeHelper.GetBrush("TextSecondaryBrush", Brushes.Gray);
                InvitationActionResultText.Text = "Connection request declined.";

                await LoadPendingInvitationsAsync();
            }
        }
    }
}
