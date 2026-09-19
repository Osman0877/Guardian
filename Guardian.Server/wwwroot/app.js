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
    fetchInvitations();
    fetchSchedules();
    fetchAppRules();
    fetchWebRules();
    fetchTimeRequests();
    fetchAfkLogs();
    fetchRecentCommands();
    fetchActivityLogs();
}

async function fetchInvitations() {
    try {
        const headers = { 'Authorization': `Bearer ${authToken}` };
        const res = await fetch('/api/family/invitations', { headers });
        const invitations = await res.json();
        renderInvitationsList(invitations || []);
    } catch (err) {
        console.error('Failed to fetch invitations:', err);
    }
}

function renderInvitationsList(invitations) {
    const container = document.getElementById('invitationsList');
    if (!container) return;
    container.innerHTML = '';

    if (!invitations || invitations.length === 0) {
        container.innerHTML = '<div class="empty-state">No connection requests sent yet.</div>';
        return;
    }

    invitations.forEach(inv => {
        let badgeClass = 'tag-pending';
        let badgeText = inv.status;
        if (inv.status === 'Accepted') badgeClass = 'tag-online';
        if (inv.status === 'Declined' || inv.status === 'Expired' || inv.status === 'Cancelled') badgeClass = 'tag-offline';

        const item = document.createElement('div');
        item.className = 'device-pill mt-2';
        item.style.display = 'flex';
        item.style.justifyContent = 'space-between';
        item.style.alignItems = 'center';

        let cancelBtn = inv.status === 'Pending' ? `<button class="btn btn-sm btn-danger" onclick="cancelInvitation('${inv.id}')">Cancel Request</button>` : '';

        item.innerHTML = `
            <div>
                <strong>Child Username: ${inv.childUsername}</strong> — <span class="tag ${badgeClass}">${badgeText}</span>
                <div class="text-muted" style="font-size: 11px; margin-top: 2px;">Sent: ${new Date(inv.createdAt).toLocaleString()} ${inv.respondedAt ? ' • Responded: ' + new Date(inv.respondedAt).toLocaleString() : ''}</div>
            </div>
            <div>${cancelBtn}</div>
        `;
        container.appendChild(item);
    });
}

async function promptConnectExistingChild() {
    const username = prompt("Enter Child's Guardian Username (e.g. Kid123):");
    if (!username || !username.trim()) return;

    try {
        const res = await fetch('/api/family/invitations', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({ childUsername: username.trim() })
        });
        const data = await res.json();
        if (res.ok && data.success) {
            showToast(`✓ Connection request sent to ${username.trim()}! Status: Pending`);
            fetchInvitations();
        } else {
            alert(data.message || 'Failed to send connection request.');
        }
    } catch (err) {
        console.error('Error sending invitation:', err);
    }
}

async function cancelInvitation(id) {
    if (!confirm('Are you sure you want to cancel this connection request?')) return;
    try {
        const res = await fetch(`/api/family/invitations/${id}/cancel`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        const data = await res.json();
        if (res.ok && data.success) {
            showToast('✓ Connection request cancelled.');
            fetchInvitations();
        } else {
            alert(data.message || 'Failed to cancel invitation.');
        }
    } catch (err) {
        console.error('Error cancelling invitation:', err);
    }
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
        container.innerHTML = '<div class="empty-state">No connected children yet. Click "+ Connect Existing Child" above or have your child accept a connection request.</div>';
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

let currentSchedules = [];
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

async function fetchSchedules() {
    try {
        const headers = { 'Authorization': `Bearer ${authToken}` };
        const res = await fetch('/api/schedules', { headers });
        if (res.ok) {
            currentSchedules = await res.json();
            renderSchedulesGrid(currentSchedules);
        }
    } catch (err) {
        console.error('Failed to fetch schedules:', err);
    }
}

function renderSchedulesGrid(schedules) {
    const container = document.getElementById('schedulesGrid');
    if (!container) return;
    container.innerHTML = '';

    const order = [1, 2, 3, 4, 5, 6, 0]; // Mon -> Sun
    
    order.forEach(dayIndex => {
        let s = (schedules || []).find(x => (x.day !== undefined ? x.day : x.Day) === dayIndex);
        if (!s) {
            s = {
                day: dayIndex,
                limitMinutes: 120,
                hasAllowedHours: false,
                allowedStartTime: '17:00:00',
                allowedEndTime: '20:00:00',
                bedtimeEnabled: false,
                bedtime: '21:30:00'
            };
        }

        const limitMins = s.limitMinutes !== undefined ? s.limitMinutes : (s.LimitMinutes || 120);
        const bedtimeEnabled = s.bedtimeEnabled !== undefined ? s.bedtimeEnabled : (s.BedtimeEnabled || false);
        const bedtimeVal = formatTimeForInput(s.bedtime || s.Bedtime || '21:30:00');
        const hasAllowed = s.hasAllowedHours !== undefined ? s.hasAllowedHours : (s.HasAllowedHours || false);
        const startTimeVal = formatTimeForInput(s.allowedStartTime || s.AllowedStartTime || '17:00:00');
        const endTimeVal = formatTimeForInput(s.allowedEndTime || s.AllowedEndTime || '20:00:00');

        const card = document.createElement('div');
        card.className = 'card mt-3';
        card.style.background = 'rgba(255, 255, 255, 0.03)';
        card.style.border = '1px solid rgba(255, 255, 255, 0.1)';

        card.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid rgba(255,255,255,0.08); padding-bottom: 10px; margin-bottom: 12px;">
                <h4 style="margin: 0; font-size: 16px;">📅 ${DAY_NAMES[dayIndex]}</h4>
                <span class="text-muted" style="font-size: 13px;" id="sched-formatted-${dayIndex}">${formatMinutesToHours(limitMins)}</span>
            </div>
            
            <div class="form-row" style="gap: 15px; align-items: flex-start; display: flex; flex-wrap: wrap;">
                <!-- 1. Daily Limit -->
                <div class="form-group flex-1" style="min-width: 180px;">
                    <label>Daily Limit (Minutes)</label>
                    <input type="number" id="sched-limit-${dayIndex}" class="form-control" value="${limitMins}" min="0" max="1440" step="15" onchange="updateSchedFormatted(${dayIndex})">
                </div>

                <!-- 2. Bedtime Limit -->
                <div class="form-group flex-1" style="min-width: 220px; background: rgba(0,0,0,0.2); padding: 12px; border-radius: 8px; border: 1px solid rgba(255,255,255,0.05);">
                    <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-weight: 600;">
                        <input type="checkbox" id="sched-bedtime-toggle-${dayIndex}" ${bedtimeEnabled ? 'checked' : ''} onchange="toggleSchedSection(${dayIndex}, 'bedtime')">
                        🌙 Enable Bedtime Lock
                    </label>
                    <div id="sched-bedtime-box-${dayIndex}" style="margin-top: 8px; display: ${bedtimeEnabled ? 'block' : 'none'};">
                        <label style="font-size: 11px; color: #aaa;">Bedtime Lock Time:</label>
                        <input type="time" id="sched-bedtime-time-${dayIndex}" class="form-control" value="${bedtimeVal}">
                    </div>
                </div>

                <!-- 3. School Time / Allowed Hours -->
                <div class="form-group flex-1" style="min-width: 250px; background: rgba(0,0,0,0.2); padding: 12px; border-radius: 8px; border: 1px solid rgba(255,255,255,0.05);">
                    <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-weight: 600;">
                        <input type="checkbox" id="sched-school-toggle-${dayIndex}" ${hasAllowed ? 'checked' : ''} onchange="toggleSchedSection(${dayIndex}, 'school')">
                        🏫 Enable School Time / Allowed Hours
                    </label>
                    <div id="sched-school-box-${dayIndex}" style="margin-top: 8px; display: ${hasAllowed ? 'block' : 'none'};">
                        <label style="font-size: 11px; color: #aaa;">Allowed Range (Start to End):</label>
                        <div style="display: flex; gap: 5px;">
                            <input type="time" id="sched-school-start-${dayIndex}" class="form-control" value="${startTimeVal}">
                            <input type="time" id="sched-school-end-${dayIndex}" class="form-control" value="${endTimeVal}">
                        </div>
                    </div>
                </div>
            </div>
        `;
        container.appendChild(card);
    });
}

function formatMinutesToHours(mins) {
    mins = parseInt(mins) || 0;
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return `${h}h ${m < 10 ? '0' : ''}${m}m limit`;
}

function updateSchedFormatted(dayIndex) {
    const val = document.getElementById(`sched-limit-${dayIndex}`).value;
    const el = document.getElementById(`sched-formatted-${dayIndex}`);
    if (el) el.innerText = formatMinutesToHours(val);
}

function toggleSchedSection(dayIndex, section) {
    if (section === 'bedtime') {
        const chk = document.getElementById(`sched-bedtime-toggle-${dayIndex}`).checked;
        document.getElementById(`sched-bedtime-box-${dayIndex}`).style.display = chk ? 'block' : 'none';
    } else if (section === 'school') {
        const chk = document.getElementById(`sched-school-toggle-${dayIndex}`).checked;
        document.getElementById(`sched-school-box-${dayIndex}`).style.display = chk ? 'block' : 'none';
    }
}

function formatTimeForInput(timeStr) {
    if (!timeStr) return '21:30';
    if (timeStr.length >= 5) return timeStr.substring(0, 5);
    return timeStr;
}

async function saveAllSchedules() {
    const order = [0, 1, 2, 3, 4, 5, 6];
    const payload = [];

    order.forEach(dayIndex => {
        const limitMins = parseInt(document.getElementById(`sched-limit-${dayIndex}`)?.value || '120');
        const bedtimeEnabled = document.getElementById(`sched-bedtime-toggle-${dayIndex}`)?.checked || false;
        const bedtimeVal = (document.getElementById(`sched-bedtime-time-${dayIndex}`)?.value || '21:30') + ':00';
        
        const hasAllowed = document.getElementById(`sched-school-toggle-${dayIndex}`)?.checked || false;
        const startTimeVal = (document.getElementById(`sched-school-start-${dayIndex}`)?.value || '17:00') + ':00';
        const endTimeVal = (document.getElementById(`sched-school-end-${dayIndex}`)?.value || '20:00') + ':00';

        payload.push({
            Day: dayIndex,
            LimitMinutes: limitMins,
            HasAllowedHours: hasAllowed,
            AllowedStartTime: startTimeVal,
            AllowedEndTime: endTimeVal,
            BedtimeEnabled: bedtimeEnabled,
            Bedtime: bedtimeVal
        });
    });

    try {
        const res = await fetch('/api/schedules', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify(payload)
        });

        const data = await res.json();
        if (res.ok && data.success) {
            showToast('✓ Schedules, Bedtime limits & School-Time rules saved & applied!');
            fetchSchedules();
        } else {
            alert(data.message || 'Failed to save schedules.');
        }
    } catch (err) {
        console.error('Error saving schedules:', err);
        alert('Failed to connect to server to save schedules.');
    }
}
