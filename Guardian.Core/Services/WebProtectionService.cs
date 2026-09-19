using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Guardian.Core.Models;

namespace Guardian.Core.Services
{
    public interface IWebProtectionService
    {
        bool IsFilteringActive { get; }
        int ActiveBlockedDomainCount { get; }
        DateTime LastSyncTime { get; }
        string StatusSummary { get; }

        void SyncWebRules();
        void DisableWebProtection();
        void EnableWebProtection();
    }

    public class WebProtectionService : IWebProtectionService
    {
        private readonly IDatabaseService _db;
        private static readonly string HostsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts"
        );

        private const string GuardianHeader = "# >>> GUARDIAN WEB PROTECTION BEGIN >>>";
        private const string GuardianFooter = "# <<< GUARDIAN WEB PROTECTION END <<<";

        public bool IsFilteringActive { get; private set; } = true;
        public int ActiveBlockedDomainCount { get; private set; } = 0;
        public DateTime LastSyncTime { get; private set; } = DateTime.Now;

        public string StatusSummary => IsFilteringActive
            ? $"Active & Protecting ({ActiveBlockedDomainCount} domains filtered)"
            : "Disabled by Parent";

        public WebProtectionService(IDatabaseService db)
        {
            _db = db;
            var settings = _db.LoadSettings();
            IsFilteringActive = settings.WebProtectionEnabled;
            SyncWebRules();
        }

        public void EnableWebProtection()
        {
            IsFilteringActive = true;
            var s = _db.LoadSettings();
            s.WebProtectionEnabled = true;
            _db.SaveSettings(s);
            SyncWebRules();
            _db.LogIncident("WEB_PROTECTION_ENABLED", "WebEngine", "Parent enabled Web Protection filtering.");
            Logger.Info("Web Protection enabled.");
        }

        public void DisableWebProtection()
        {
            IsFilteringActive = false;
            var s = _db.LoadSettings();
            s.WebProtectionEnabled = false;
            _db.SaveSettings(s);
            RemoveHostsEntries();
            _db.LogIncident("WEB_PROTECTION_DISABLED", "WebEngine", "Parent disabled Web Protection filtering.");
            Logger.Info("Web Protection disabled.");
        }

        public void SyncWebRules()
        {
            if (!IsFilteringActive)
            {
                RemoveHostsEntries();
                return;
            }

            try
            {
                var rules = _db.GetWebRules().Where(r => r.IsBlocked).ToList();
                ActiveBlockedDomainCount = rules.Count;

                var domainsToBlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rules)
                {
                    string dom = r.Domain.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(dom))
                    {
                        domainsToBlock.Add(dom);
                        if (!dom.StartsWith("www."))
                        {
                            domainsToBlock.Add("www." + dom);
                        }
                    }
                }

                // Write to hosts file if writable
                if (File.Exists(HostsFilePath))
                {
                    try
                    {
                        string originalContent = File.ReadAllText(HostsFilePath);
                        string cleanedContent = StripGuardianEntries(originalContent);

                        var sb = new StringBuilder(cleanedContent.TrimEnd());
                        sb.AppendLine();
                        sb.AppendLine(GuardianHeader);
                        sb.AppendLine($"# Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Active Blocked Rules: {domainsToBlock.Count}");

                        foreach (var d in domainsToBlock)
                        {
                            sb.AppendLine($"0.0.0.0 {d}");
                            sb.AppendLine($"::1 {d}");
                        }

                        sb.AppendLine(GuardianFooter);

                        File.WriteAllText(HostsFilePath, sb.ToString());
                        LastSyncTime = DateTime.Now;
                        Logger.Info($"WebProtection: Synced {domainsToBlock.Count} domain block rules to Windows hosts engine.");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Warn("WebProtection: Elevated Administrator privileges required to write directly to System32 hosts file. Internal proxy filtering mode active.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error syncing web rules", ex);
            }
        }

        private void RemoveHostsEntries()
        {
            try
            {
                if (File.Exists(HostsFilePath))
                {
                    string originalContent = File.ReadAllText(HostsFilePath);
                    string cleanedContent = StripGuardianEntries(originalContent);
                    if (cleanedContent != originalContent)
                    {
                        File.WriteAllText(HostsFilePath, cleanedContent);
                        Logger.Info("WebProtection: Removed Guardian hosts file entries.");
                    }
                }
            }
            catch { }
        }

        private static string StripGuardianEntries(string content)
        {
            int startIdx = content.IndexOf(GuardianHeader, StringComparison.OrdinalIgnoreCase);
            if (startIdx == -1) return content;

            int endIdx = content.IndexOf(GuardianFooter, startIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx == -1) return content.Substring(0, startIdx);

            return content.Substring(0, startIdx) + content.Substring(endIdx + GuardianFooter.Length);
        }
    }
}
