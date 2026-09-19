let authToken = localStorage.getItem('guardian_token') || null;
let currentUsername = localStorage.getItem('guardian_username') || 'Parent';
let currentUserId = localStorage.getItem('guardian_userid') || '';
let currentRole = localStorage.getItem('guardian_role') || 'Parent';
let signalrConnection = null;
let currentAuthMode = 'login';
let activeDevicesList = [];

document.addEventListener('DOMContentLoaded', () => {
    if (authToken) {
        showApp();
    } else {
        showLogin();
    }
});

function toggleAuthMode(mode) {
    currentAuthMode = mode;
    const btnLogin = document.getElementById('tabBtnLogin');
    const btnRegister = document.getElementById('tabBtnRegister');
    const title = document.getElementById('authCardTitle');
    const subtitle = document.getElementById('authCardSubtitle');
    const submitBtn = document.getElementById('loginBtn');
    const errEl = document.getElementById('loginError');
    const succEl = document.getElementById('loginSuccess');

    errEl.style.display = 'none';
    succEl.style.display = 'none';

    if (mode === 'register') {
        btnLogin.className = 'btn btn-sm btn-secondary';
        btnRegister.className = 'btn btn-sm btn-primary';
        title.innerText = 'Create Parent Account';
        subtitle.innerText = 'Register your main parent master account';
        submitBtn.innerText = 'Create Account';
    } else {
        btnLogin.className = 'btn btn-sm btn-primary';
        btnRegister.className = 'btn btn-sm btn-secondary';
        title.innerText = 'Parent Login';
        subtitle.innerText = 'Manage family safety and child devices remotely';
        submitBtn.innerText = 'Log In';
    }
}

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

async function handleAuthSubmit(e) {
    e.preventDefault();
    const user = document.getElementById('username').value.trim();
    const pass = document.getElementById('password').value.trim();
    const errEl = document.getElementById('loginError');
    const succEl = document.getElementById('loginSuccess');
    const btn = document.getElementById('loginBtn');

    errEl.style.display = 'none';
    succEl.style.display = 'none';
    btn.disabled = true;

    const endpoint = currentAuthMode === 'register' ? '/api/auth/register' : '/api/auth/login';

    try {
        const res = await fetch(endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: user, password: pass })
        });

        const data = await res.json();
        if (res.ok && data.success) {
            if (currentAuthMode === 'register') {
                succEl.innerText = data.message || 'Account created! You can now log in.';
                succEl.style.display = 'block';
                toggleAuthMode('login');
            } else {
                authToken = data.token;
                currentUsername = data.username;
                currentUserId = data.userId;
                currentRole = data.role;
                localStorage.setItem('guardian_token', authToken);
                localStorage.setItem('guardian_username', currentUsername);
                localStorage.setItem('guardian_userid', currentUserId);
                localStorage.setItem('guardian_role', currentRole);
                showApp();
            }
        } else {
            errEl.innerText = data.message || 'Authentication failed.';
            errEl.style.display = 'block';
        }
    } catch (err) {
        errEl.innerText = 'Unable to connect to Guardian server. Please verify backend is running.';
        errEl.style.display = 'block';
    } finally {
        btn.disabled = false;
    }
}

function handleLogout() {
    authToken = null;
    localStorage.removeItem('guardian_token');
    localStorage.removeItem('guardian_username');
    localStorage.removeItem('guardian_userid');
    localStorage.removeItem('guardian_role');
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
        fetchChildrenAndDevices();
    });

    signalrConnection.on('CommandStatusChanged', (data) => {
        showToast(`Command [${data.commandId.substring(0, 8)}] Status: ${data.status}`);
        fetchRecentCommands();
    });

    signalrConnection.start()
        .then(() => signalrConnection.invoke('RegisterParent', currentUserId || 'default_parent'))
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
        const headers = { 'Authorization': `Bearer ${authToken}` };
        const [resChildren, resDevices] = await Promise.all([
            fetch('/api/family/children', { headers }),
            fetch('/api/family/devices', { headers })
        ]);

        const children = await resChildren.json();
        const devices = await resDevices.json();
        activeDevicesList = devices || [];

        renderChildrenGrid(children, devices);
        updateDeviceStatusUI(devices.length > 0 ? devices[0] : null);

        const onlineCount = devices.filter(d => d.isOnline).length;
        document.getElementById('statOnlineDevices').innerText = `${onlineCount} Online`;
    } catch (err) {
        console.error('Failed to fetch children and devices:', err);
    }
}

function renderChildrenGrid(children, devices) {
    const container = document.getElementById('childrenList');
    const childSelect = document.getElementById('screenTimeChildSelect');

    container.innerHTML = '';
    if (childSelect) childSelect.innerHTML = '<option value="">Select Child</option>';

    if (!children || children.length === 0) {
        container.innerHTML = '<div class="empty-state">No linked child profiles yet. Click "+ Add Child Profile" above to create one.</div>';
        return;
    }

    children.forEach(c => {
        if (childSelect) {
            const opt = document.createElement('option');
            opt.value = c.id;
            opt.innerText = c.name;
            childSelect.appendChild(opt);
        }

        const childDevs = (devices || []).filter(d => d.childId === c.id);
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
                    <h3>${c.avatar || '👦'} ${c.name}</h3>
                    <p class="text-muted">Daily Limit: ${c.dailyLimitMinutes} minutes</p>
                </div>
                <button class="btn btn-sm btn-primary" onclick="switchTab('screentime')">Manage</button>
            </div>
            <div class="mt-2">${devHtml || '<span class="text-muted">No connected devices registered for this child</span>'}</div>
        `;
        container.appendChild(card);
    });
}

function updateDeviceStatusUI(d) {
    if (!d) {
        document.getElementById('heroDeviceName').innerText = 'No Connected Device';
        document.getElementById('heroDeviceStatus').className = 'tag tag-offline';
        document.getElementById('heroDeviceStatus').innerText = '○ Offline';
        document.getElementById('heroLastSeen').innerText = 'Never';
        document.getElementById('heroCurrentApp').innerText = 'No activity recorded';
        document.getElementById('statScreenTime').innerText = 'No activity recorded';
        document.getElementById('screenTimePercentage').innerText = '0%';
        document.getElementById('screenTimeProgressFill').style.width = '0%';
        document.getElementById('statProtection').innerText = 'No Device Active';
        return;
    }

    document.getElementById('heroDeviceName').innerText = d.deviceName || d.deviceId;
    document.getElementById('heroDeviceStatus').className = d.isOnline ? 'tag tag-online' : 'tag tag-offline';
    document.getElementById('heroDeviceStatus').innerText = d.isOnline ? '● Online' : '○ Offline';
    document.getElementById('heroLastSeen').innerText = d.isOnline ? 'Just now' : new Date(d.lastSeen).toLocaleTimeString();
    document.getElementById('heroCurrentApp').innerText = d.currentApp || 'Idle';

    const usedMins = Math.floor(d.screenTimeUsedSeconds / 60);
    const limitMins = d.dailyLimitMinutes || 120;
    document.getElementById('statScreenTime').innerText = `${Math.floor(usedMins / 60)}h ${usedMins % 60}m / ${Math.floor(limitMins / 60)}h ${limitMins % 60}m`;

    const pct = Math.min(100, Math.round((usedMins / limitMins) * 100));
    document.getElementById('screenTimePercentage').innerText = `${pct}%`;
    document.getElementById('screenTimeProgressFill').style.width = `${pct}%`;

    document.getElementById('statProtection').innerText = d.isProtected ? 'Active & Healthy' : 'Warning: Unprotected';
}

async function promptCreateChildAccount() {
    const name = prompt('Enter Child Profile Name (e.g. Alex):', 'Alex');
    if (!name) return;
    const user = prompt('Enter Child Login Username (for child device login):', 'alex_child');
    if (!user) return;
    const pass = prompt('Enter Child Login Password:', 'Password123');
    if (!pass) return;

    try {
        const res = await fetch('/api/auth/create-child', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({ name, username: user, password: pass })
        });
        const data = await res.json();
        if (res.ok && data.success) {
            showToast('✓ Child profile and account created successfully!');
            fetchChildrenAndDevices();
        } else {
            alert(data.message || 'Failed to create child account.');
        }
    } catch (err) {
        console.error('Error creating child account:', err);
    }
}

async function triggerRemoteLock() {
    if (activeDevicesList.length === 0) {
        alert('No registered device connected to send Lock command.');
        return;
    }
    document.getElementById('lockModal').style.display = 'flex';
}

function closeLockModal() {
    document.getElementById('lockModal').style.display = 'none';
}

async function confirmRemoteLock() {
    closeLockModal();
    const res = await sendRemoteCommand('LockDevice', {});
    if (res.success) {
        showToast('✓ Lock command queued for child device!');
    }
}

async function triggerRemoteUnlock() {
    if (activeDevicesList.length === 0) {
        alert('No registered device connected to send Unlock command.');
        return;
    }
    const res = await sendRemoteCommand('UnlockDevice', {});
    if (res.success) {
        showToast('✓ Unlock command queued for child device!');
    }
}

async function triggerRefreshPolicy() {
    if (activeDevicesList.length === 0) {
        alert('No registered device connected to send Refresh Policy command.');
        return;
    }
    const res = await sendRemoteCommand('RefreshPolicy', {});
    if (res.success) {
        showToast('✓ Policy refresh command queued for child device!');
    }
}

function toggleRemoteAfkModal() {
    if (activeDevicesList.length === 0) {
        alert('No registered device connected to send AFK command.');
        return;
    }
    document.getElementById('afkModal').style.display = 'flex';
}

function closeAfkModal() {
    document.getElementById('afkModal').style.display = 'none';
}

async function confirmRemoteAfk(activate) {
    closeAfkModal();
    const duration = parseInt(document.getElementById('afkDurationSelect').value);
    const type = activate ? 'StartAfk' : 'EndAfk';
    const payload = activate ? { durationMinutes: duration } : {};

    const res = await sendRemoteCommand(type, payload);
    if (res.success) {
        showToast(activate ? '✓ AFK Mode queued remotely!' : '✓ AFK Mode disable queued remotely!');
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
    const res = await sendRemoteCommand('AddTime', { minutes: minutes });
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
    const proc = prompt('Enter application process name (e.g. chrome.exe or game.exe):', 'chrome.exe');
    if (!proc) return;
    const disp = prompt('Enter friendly application name:', 'Google Chrome');

    const res = await sendRemoteCommand('BlockApplication', { processName: proc, displayName: disp || proc });
    if (res.success) {
        showToast(`✓ Application ${disp || proc} blocked successfully!`);
        fetchAppRules();
    }
}

async function sendRemoteCommand(commandType, payloadObj) {
    if (!activeDevicesList || activeDevicesList.length === 0) {
        showToast('No registered child device available.');
        return { success: false };
    }

    const targetDeviceId = activeDevicesList[0].deviceId;
    const targetChildId = activeDevicesList[0].childId || 'default';

    try {
        const res = await fetch('/api/commands/create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({
                childId: targetChildId,
                deviceId: targetDeviceId,
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
            <span class="status-badge status-${(c.status || '').toString().toLowerCase()}">${c.status}</span>
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

    if (!pending || pending.length === 0) {
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
                    <p class="text-muted">App: <strong>${r.targetProcessName || 'Application'}</strong> • Requested: ${r.requestedMinutes} minutes</p>
                    <p class="italic">"${r.reason || 'Extra screen time request'}"</p>
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

        const tbody = document.getElementById('appRulesTableBody');
        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" class="text-center text-muted">No application rules configured yet.</td></tr>';
            return;
        }

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
    const type = block ? 'BlockApplication' : 'UnblockApplication';
    const res = await sendRemoteCommand(type, { processName });
    if (res.success) {
        showToast(block ? `✓ Blocked ${processName}` : `✓ Unblocked ${processName}`);
        fetchAppRules();
    }
}

async function fetchWebRules() {
    try {
        const res = await fetch('/api/family/web-rules');
        const list = await res.json();

        const tbody = document.getElementById('webRulesTableBody');
        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No blocked website domains configured.</td></tr>';
            return;
        }

        tbody.innerHTML = list.map(w => `
            <tr>
                <td><strong>${w.domain}</strong></td>
                <td>${w.category || 'General'}</td>
                <td>${w.reason || 'Blocked'}</td>
                <td>
                    <button class="btn btn-sm btn-success" onclick="unblockWebsite('${w.domain}')">Unblock</button>
                </td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Error fetching web rules:', err);
    }
}

async function unblockWebsite(domain) {
    const res = await sendRemoteCommand('UnblockWebsite', { domain });
    if (res.success) {
        showToast(`✓ Unblocked domain ${domain}`);
        fetchWebRules();
    }
}

async function fetchAfkLogs() {
    try {
        const res = await fetch('/api/afklog/sessions');
        const list = await res.json();

        const tbody = document.getElementById('afkLogsTableBody');
        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No AFK sessions recorded yet.</td></tr>';
            return;
        }

        tbody.innerHTML = list.slice(0, 10).map(s => `
            <tr>
                <td><code>${s.id.substring(0, 8)}</code></td>
                <td>${s.formattedStartDate} ${s.formattedStartTime}</td>
                <td>${s.formattedEndDate} ${s.formattedEndTime}</td>
                <td>${s.formattedDuration}</td>
                <td>${s.activatedBy}</td>
                <td><span class="${s.endReason === 'Active' ? 'tag tag-online' : 'tag tag-offline'}">${s.endReason}</span></td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Error fetching AFK logs:', err);
    }
}

async function fetchActivityLogs() {
    try {
        const res = await fetch('/api/activity/incidents');
        const list = await res.json();
        const feed = document.getElementById('activityStreamFeed');

        if (!list || list.length === 0) {
            feed.innerHTML = '<div class="empty-state">No activity recorded yet.</div>';
            return;
        }

        feed.innerHTML = list.slice(0, 10).map(a => `
            <div class="feed-item">
                <span class="text-muted">[${new Date(a.timestamp).toLocaleTimeString()}]</span>
                <strong>${a.eventType}</strong> — ${a.description}
            </div>
        `).join('');
    } catch (err) {
        console.error('Error fetching activity logs:', err);
    }
}

function switchTab(tabName) {
    document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));

    const activeTab = document.getElementById(`tab-${tabName}`);
    if (activeTab) activeTab.classList.add('active');

    const activeNav = Array.from(document.querySelectorAll('.nav-item')).find(el => el.getAttribute('href') === `#${tabName}`);
    if (activeNav) activeNav.classList.add('active');
}

function showToast(msg) {
    const banner = document.getElementById('notificationBanner');
    banner.innerText = msg;
    banner.style.display = 'block';
    setTimeout(() => { banner.style.display = 'none'; }, 4000);
}
