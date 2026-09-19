let authToken = localStorage.getItem('guardian_token') || null;
let currentUsername = localStorage.getItem('guardian_username') || 'MasterDad';
let signalrConnection = null;

document.addEventListener('DOMContentLoaded', () => {
    if (authToken) {
        showApp();
    } else {
        showLogin();
    }
});

function showLogin() {
    document.getElementById('loginView').style.display = 'flex';
    document.getElementById('appView').style.display = 'none';
}

function showApp() {
    document.getElementById('loginView').style.display = 'none';
    document.getElementById('appView').style.display = 'flex';
    document.getElementById('parentNameDisplay').innerText = currentUsername;

    initSignalR();
    loadDashboardData();
}

async function handleLogin(e) {
    e.preventDefault();
    const user = document.getElementById('username').value.trim();
    const pass = document.getElementById('password').value.trim();
    const errEl = document.getElementById('loginError');
    const btn = document.getElementById('loginBtn');

    errEl.style.display = 'none';
    btn.disabled = true;
    btn.innerText = 'Logging In...';

    try {
        const res = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: user, password: pass })
        });

        const data = await res.json();
        if (res.ok && data.success) {
            authToken = data.token;
            currentUsername = data.username;
            localStorage.setItem('guardian_token', authToken);
            localStorage.setItem('guardian_username', currentUsername);
            showApp();
        } else {
            errEl.innerText = data.message || 'Login failed.';
            errEl.style.display = 'block';
        }
    } catch (err) {
        errEl.innerText = 'Unable to connect to Guardian server. Please verify backend is running.';
        errEl.style.display = 'block';
    } finally {
        btn.disabled = false;
        btn.innerText = 'Log In';
    }
}

function handleLogout() {
    authToken = null;
    localStorage.removeItem('guardian_token');
    localStorage.removeItem('guardian_username');
    if (signalrConnection) {
        signalrConnection.stop();
    }
    showLogin();
}

function initSignalR() {
    if (typeof signalR === 'undefined') return;

    signalrConnection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/guardian')
        .withAutomaticReconnect()
        .build();

    signalrConnection.on('DeviceStatusUpdated', (status) => {
        updateDeviceStatusUI(status);
    });

    signalrConnection.on('CommandStatusChanged', (data) => {
        showToast(`Command [${data.commandId.substring(0, 8)}] Status: ${data.status}`);
        fetchRecentCommands();
    });

    signalrConnection.start()
        .then(() => signalrConnection.invoke('RegisterParent', 'default_family'))
        .catch(err => console.error('SignalR error:', err));
}

async function loadDashboardData() {
    fetchChildrenAndDevices();
    fetchAppRules();
    fetchWebRules();
    fetchTimeRequests();
    fetchAfkLogs();
    fetchRecentCommands();
    fetchActivityLogs();
}

async function fetchChildrenAndDevices() {
    try {
        const [resChildren, resDevices] = await Promise.all([
            fetch('/api/family/children'),
            fetch('/api/family/devices')
        ]);

        const children = await resChildren.json();
        const devices = await resDevices.json();

        renderChildrenGrid(children, devices);
        if (devices.length > 0) {
            updateDeviceStatusUI(devices[0]);
        }
    } catch (err) {
        console.error('Failed to fetch children and devices:', err);
    }
}

function renderChildrenGrid(children, devices) {
    const container = document.getElementById('childrenList');
    container.innerHTML = '';

    children.forEach(c => {
        const childDevs = devices.filter(d => d.childId === c.id || c.id === 'default');
        let devHtml = childDevs.map(d => `
            <div class="device-pill">
                <span class="${d.isOnline ? 'tag tag-online' : 'tag tag-offline'}">${d.isOnline ? '● Online' : '○ Offline'}</span>
                <strong>${d.deviceName}</strong> — ${d.currentApp || 'Idle'}
            </div>
        `).join('');

        const card = document.createElement('div');
        card.className = 'card mt-2';
        card.innerHTML = `
            <div class="card-header">
                <div>
                    <h3>👶 ${c.name}</h3>
                    <p class="text-muted">Daily Limit: ${c.dailyLimitMinutes} minutes</p>
                </div>
                <button class="btn btn-sm btn-primary" onclick="switchTab('screentime')">Manage</button>
            </div>
            <div class="mt-2">${devHtml || '<span class="text-muted">No connected devices</span>'}</div>
        `;
        container.appendChild(card);
    });
}

function updateDeviceStatusUI(d) {
    document.getElementById('heroDeviceName').innerText = d.deviceName || 'Alex Gaming PC';
    document.getElementById('heroDeviceStatus').className = d.isOnline ? 'tag tag-online' : 'tag tag-offline';
    document.getElementById('heroDeviceStatus').innerText = d.isOnline ? '● Online' : '○ Offline';
    document.getElementById('heroLastSeen').innerText = d.isOnline ? 'Just now' : new Date(d.lastSeen).toLocaleTimeString();
    document.getElementById('heroCurrentApp').innerText = d.currentApp || 'Idle';

    const usedMins = Math.floor(d.screenTimeUsedSeconds / 60);
    const limitMins = 120;
    document.getElementById('statScreenTime').innerText = `${Math.floor(usedMins / 60)}h ${usedMins % 60}m / 2h 00m`;

    const pct = Math.min(100, Math.round((usedMins / limitMins) * 100));
    document.getElementById('screenTimePercentage').innerText = `${pct}%`;
    document.getElementById('screenTimeProgressFill').style.width = `${pct}%`;

    document.getElementById('statProtection').innerText = d.isProtected ? 'Active & Healthy' : 'Warning: Unprotected';
}

async function triggerRemoteLock() {
    document.getElementById('lockModal').style.display = 'flex';
}

function closeLockModal() {
    document.getElementById('lockModal').style.display = 'none';
}

async function confirmRemoteLock() {
    closeLockModal();
    const res = await sendRemoteCommand('LockDevice', {});
    if (res.success) {
        showToast('✓ Lock command sent to child device!');
    }
}

function toggleRemoteAfkModal() {
    document.getElementById('afkModal').style.display = 'flex';
}

function closeAfkModal() {
    document.getElementById('afkModal').style.display = 'none';
}

async function confirmRemoteAfk(activate) {
    closeAfkModal();
    const duration = parseInt(document.getElementById('afkDurationSelect').value);
    const type = activate ? 'ActivateAfk' : 'DeactivateAfk';
    const payload = activate ? { durationMinutes: duration } : {};

    const res = await sendRemoteCommand(type, payload);
    if (res.success) {
        showToast(activate ? '✓ AFK Mode activated remotely!' : '✓ AFK Mode disabled remotely!');
    }
}

async function saveNewDailyLimit() {
    const hours = parseFloat(document.getElementById('newDailyLimitInput').value);
    const limitMins = Math.round(hours * 60);
    const res = await sendRemoteCommand('SetDailyLimit', { dailyLimitMinutes: limitMins });
    if (res.success) {
        showToast(`✓ Daily limit updated to ${hours} hours!`);
    }
}

async function addExtraTime(minutes) {
    const res = await sendRemoteCommand('GrantBonusTime', { minutes: minutes });
    if (res.success) {
        showToast(`✓ Added ${minutes} extra minutes to child screen time!`);
    }
}

function promptCustomExtraTime() {
    const input = prompt('Enter custom extra minutes to grant:', '45');
    if (input && !isNaN(input)) {
        addExtraTime(parseInt(input));
    }
}

async function blockWebsite() {
    const input = document.getElementById('webDomainInput');
    const domain = input.value.trim();
    if (!domain) return;

    const res = await sendRemoteCommand('BlockWebsite', { domain: domain });
    if (res.success) {
        showToast(`✓ Website ${domain} blocked successfully!`);
        input.value = '';
        fetchWebRules();
    }
}

async function promptBlockCustomApp() {
    const proc = prompt('Enter application process name (e.g. roblox.exe or chrome.exe):', 'roblox.exe');
    if (!proc) return;
    const disp = prompt('Enter friendly application name:', 'Roblox');

    const res = await sendRemoteCommand('BlockApp', { processName: proc, displayName: disp || proc });
    if (res.success) {
        showToast(`✓ Application ${disp || proc} blocked successfully!`);
        fetchAppRules();
    }
}

async function sendRemoteCommand(commandType, payloadObj) {
    try {
        const res = await fetch('/api/commands/create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({
                childId: 'default',
                deviceId: 'Alex-Gaming-PC',
                commandType: commandType,
                payloadJson: JSON.stringify(payloadObj)
            })
        });

        const data = await res.json();
        fetchRecentCommands();
        return data;
    } catch (err) {
        console.error('Error sending remote command:', err);
        showToast('Error dispatching remote command to backend.');
        return { success: false };
    }
}

async function fetchRecentCommands() {
    try {
        const res = await fetch('/api/commands/recent');
        const list = await res.json();
        renderCommandFeed(list);
    } catch (err) {
        console.error('Error fetching commands:', err);
    }
}

function renderCommandFeed(list) {
    const feed = document.getElementById('commandStatusFeed');
    if (!list || list.length === 0) {
        feed.innerHTML = '<div class="empty-state">No recent commands dispatched.</div>';
        return;
    }

    feed.innerHTML = list.slice(0, 5).map(c => `
        <div class="feed-item">
            <span class="status-badge status-${c.status.toLowerCase()}">${c.status}</span>
            <strong>${c.commandType}</strong>
            <span class="text-muted">— ${new Date(c.createdAt).toLocaleTimeString()}</span>
        </div>
    `).join('');
}

async function fetchTimeRequests() {
    try {
        const res = await fetch('/api/requests');
        const list = await res.json();
        const pending = list.filter(r => r.status === 'Pending' || r.status === 0);

        document.getElementById('requestBadge').innerText = pending.length;
        renderRequestsFeed(pending);
    } catch (err) {
        console.error('Error fetching time requests:', err);
    }
}

function renderRequestsFeed(pending) {
    const qList = document.getElementById('quickRequestsList');
    const aList = document.getElementById('allRequestsList');

    if (pending.length === 0) {
        const emptyHtml = '<div class="empty-state">No pending time requests right now.</div>';
        qList.innerHTML = emptyHtml;
        aList.innerHTML = emptyHtml;
        return;
    }

    const html = pending.map(r => `
        <div class="card mt-2">
            <div class="card-header">
                <div>
                    <h4>📩 Request from ${r.childName}</h4>
                    <p class="text-muted">App: <strong>${r.targetProcessName || 'Roblox'}</strong> • Requested: ${r.requestedMinutes} minutes</p>
                    <p class="italic">"${r.reason || 'Can I finish my game?'}"</p>
                </div>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-success" onclick="respondToRequest('${r.id}', true, ${r.requestedMinutes})">Approve ${r.requestedMinutes}m</button>
                    <button class="btn btn-sm btn-danger" onclick="respondToRequest('${r.id}', false, 0)">Deny</button>
                </div>
            </div>
        </div>
    `).join('');

    qList.innerHTML = html;
    aList.innerHTML = html;
}

async function respondToRequest(requestId, approved, minutes) {
    try {
        const res = await fetch(`/api/requests/${requestId}/respond`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ approved: approved, minutes: minutes })
        });
        const data = await res.json();
        showToast(data.message);
        fetchTimeRequests();
    } catch (err) {
        console.error('Error responding to request:', err);
    }
}

async function fetchAppRules() {
    try {
        const res = await fetch('/api/family/app-rules');
        const list = await res.json();

        // Include default apps if empty
        if (list.length === 0) {
            list.push(
                { processName: 'robloxplayerbeta', displayName: 'Roblox', category: 'Gaming', isBlocked: false },
                { processName: 'chrome', displayName: 'Google Chrome', category: 'Web Browser', isBlocked: false },
                { processName: 'discord', displayName: 'Discord', category: 'Chat', isBlocked: false }
            );
        }

        const tbody = document.getElementById('appRulesTableBody');
        tbody.innerHTML = list.map(a => `
            <tr>
                <td><strong>${a.displayName}</strong></td>
                <td><code>${a.processName}</code></td>
                <td>${a.category || 'General'}</td>
                <td><span class="${a.isBlocked ? 'tag tag-offline' : 'tag tag-online'}">${a.isBlocked ? 'Blocked' : 'Allowed'}</span></td>
                <td>
                    <button class="${a.isBlocked ? 'btn btn-sm btn-success' : 'btn btn-sm btn-danger'}" 
                            onclick="toggleAppRule('${a.processName}', ${!a.isBlocked})">
                        ${a.isBlocked ? 'Unblock' : 'Lock'}
                    </button>
                </td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Error fetching app rules:', err);
    }
}

async function toggleAppRule(processName, block) {
    const commandType = block ? 'BlockApp' : 'UnblockApp';
    const res = await sendRemoteCommand(commandType, { processName: processName });
    if (res.success) {
        showToast(`✓ App ${processName} ${block ? 'locked' : 'unlocked'} successfully!`);
        fetchAppRules();
    }
}

async function fetchWebRules() {
    try {
        const res = await fetch('/api/family/web-rules');
        const list = await res.json();
        const tbody = document.getElementById('webRulesTableBody');
        if (list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="4" class="text-muted">No website blocks currently active.</td></tr>';
            return;
        }

        tbody.innerHTML = list.map(w => `
            <tr>
                <td><strong>${w.domain}</strong></td>
                <td>${w.category || 'Parent Blocked'}</td>
                <td>${w.reason || 'Blocked from Web Dashboard'}</td>
                <td><button class="btn btn-sm btn-secondary" onclick="unblockWebsite('${w.domain}')">Unblock</button></td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Error fetching web rules:', err);
    }
}

async function unblockWebsite(domain) {
    const res = await sendRemoteCommand('UnblockWebsite', { domain: domain });
    if (res.success) {
        showToast(`✓ Domain ${domain} unblocked successfully!`);
        fetchWebRules();
    }
}

async function fetchAfkLogs() {
    try {
        const [resLogs, resSummary] = await Promise.all([
            fetch('/api/afk/logs'),
            fetch('/api/afk/summary')
        ]);
        const logs = await resLogs.json();
        const summary = await resSummary.json();

        document.getElementById('afkTodaySessions').innerText = `${summary.todayCount} Sessions`;
        document.getElementById('afkTodayTime').innerText = formatDuration(summary.todaySeconds);
        document.getElementById('afkTotalTime').innerText = formatDuration(summary.totalSeconds);

        const tbody = document.getElementById('afkLogsTableBody');
        if (logs.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-muted">No AFK session history recorded yet.</td></tr>';
            return;
        }

        tbody.innerHTML = logs.map(l => `
            <tr>
                <td><code>${l.id.substring(0, 8)}</code></td>
                <td>${new Date(l.startTimestamp).toLocaleString()}</td>
                <td>${l.endTimestamp ? new Date(l.endTimestamp).toLocaleString() : 'Active'}</td>
                <td>${formatDuration(l.durationSeconds)}</td>
                <td>${l.activatedBy}</td>
                <td><span class="tag tag-online">${l.endReason}</span></td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Error fetching AFK logs:', err);
    }
}

async function fetchActivityLogs() {
    try {
        const res = await fetch('/api/activity');
        const list = await res.json();
        const feed = document.getElementById('activityStreamFeed');
        if (list.length === 0) {
            feed.innerHTML = '<div class="empty-state">No recent activity events.</div>';
            return;
        }

        feed.innerHTML = list.map(a => `
            <div class="card mt-2">
                <div class="card-header">
                    <div>
                        <strong>${a.eventType}</strong> — <span>${a.description}</span>
                    </div>
                    <span class="text-muted" style="font-size:12px;">${new Date(a.timestamp).toLocaleTimeString()}</span>
                </div>
            </div>
        `).join('');
    } catch (err) {
        console.error('Error fetching activity logs:', err);
    }
}

function switchTab(tabId) {
    document.querySelectorAll('.tab-content').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));

    const activeTab = document.getElementById(`tab-${tabId}`);
    if (activeTab) activeTab.classList.add('active');

    const activeNav = document.querySelector(`.nav-item[href="#${tabId}"]`);
    if (activeNav) activeNav.classList.add('active');

    const titles = {
        'dashboard': 'Parent Dashboard',
        'children': 'Children & Connected Devices',
        'screentime': 'Screen Time Management',
        'apps': 'Application Rules & Locks',
        'websites': 'Website Rules & Filtering',
        'requests': 'Time Requests',
        'afk': 'AFK Mode & Session Log',
        'activity': 'Activity Stream & Security Log'
    };
    document.getElementById('pageTitle').innerText = titles[tabId] || 'Parent Dashboard';
}

function showToast(msg) {
    const toast = document.getElementById('notificationBanner');
    toast.innerText = msg;
    toast.style.display = 'block';
    setTimeout(() => { toast.style.display = 'none'; }, 4000);
}

function formatDuration(sec) {
    if (!sec || sec <= 0) return '0s';
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    const s = sec % 60;
    if (h > 0) return `${h}h ${m}m ${s}s`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
}
