'use strict';

/* ==================================================================== *
 *  Axumera License Manager — frontend.
 *  All security, signing, verification, persistence and file-dialog
 *  operations are performed by the C# host via the JSON bridge. This
 *  script only handles layout, navigation and visual state.
 * ==================================================================== */

/* ----------------------------- DOM helpers -------------------------- */

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') node.className = v;
    else if (k === 'html') node.innerHTML = v;
    else if (k.startsWith('on') && typeof v === 'function') node.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'type' && tag !== 'input' && tag !== 'button') node.setAttribute(k, v);
    else node.setAttribute(k, v === true ? '' : v);
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined) continue;
    node.append(child);
  }
  return node;
}

const text = (str) => document.createTextNode(String(str));

function tsInput(value, props = {}) {
  return el('input', { type: 'text', class: 'input', autocomplete: 'off', value, ...props });
}

/* ------------------------- JSON bridge to C# ------------------------ */

const pending = new Map();
let seq = 0;

function invoke(action, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage(JSON.stringify({ id, action, payload }));
  });
}

async function call(action, payload = {}) {
  try {
    return { ok: true, data: await invoke(action, payload) };
  } catch (err) {
    return { ok: false, error: err.message || 'The operation failed.' };
  }
}

window.chrome.webview.addEventListener('message', (e) => {
  let msg = e.data;
  if (typeof msg === 'string') {
    try { msg = JSON.parse(msg); } catch { return; }
  }
  if (msg && msg.kind === 'event') { onHostEvent(msg.event); return; }
  if (!msg || !pending.has(msg.id)) return;
  const p = pending.get(msg.id);
  pending.delete(msg.id);
  if (msg.ok) p.resolve(msg.data); else p.reject(new Error(msg.error || 'The operation failed.'));
});

/* ------------------------------ App state ---------------------------- */

const state = {
  app: null,
  page: 'dashboard',
  collapsed: document.documentElement.classList.contains('collapsed'),
  generate: { step: 1, school: '', hwid: '', expires: '' },
  activeSearch: '',
  activeSort: { key: 'expires', dir: 1 },
  archivedSearch: '',
  archivedSort: { key: 'createdLocal', dir: -1 },
};

const NAV = [
  { id: 'dashboard', icon: '\uD83C\uDFE0', label: 'Dashboard' },
  { id: 'generate', icon: '\u2795', label: 'Generate License' },
  { id: 'active', icon: '\uD83D\uDCCB', label: 'Active Licenses' },
  { id: 'archived', icon: '\uD83D\uDDC4', label: 'Archived Licenses' },
  { id: 'verify', icon: '\uD83D\uDD0D', label: 'Verify License' },
  { id: 'settings', icon: '\u2699', label: 'Settings' },
];

const pageEl = document.getElementById('page');
const navEl = document.getElementById('nav');
const footerEl = document.getElementById('sidebarFooter');
const toggleEl = document.getElementById('sidebarToggle');
const toggleGlyph = document.getElementById('toggleGlyph');
const modalRoot = document.getElementById('modalRoot');
const toastsEl = document.getElementById('toasts');

/* --------------------------- Sidebar shell --------------------------- */

function buildNav() {
  navEl.replaceChildren(
    ...NAV.map((item) => el('button', {
      class: 'nav-item' + (state.page === item.id ? ' active' : ''),
      type: 'button',
      title: state.collapsed ? item.label : undefined,
      'data-page': item.id,
      onclick: () => showPage(item.id),
    }, el('span', { class: 'nav-icon' }, text(item.icon)),
      el('span', { class: 'nav-label' }, text(item.label)))),
  );
}

function buildFooter(username) {
  const initials = username.trim().slice(0, 2).toUpperCase() || 'A';
  const logout = el('button', {
    class: 'btn-logout',
    type: 'button',
    title: state.collapsed ? 'Log out' : undefined,
    onclick: logoutPressed,
  }, state.collapsed ? text('\u23FB') : text('Log out'));
  footerEl.replaceChildren(
    el('div', { class: 'user-avatar', title: username }, text(initials)),
    el('div', { class: 'user-info' },
      el('span', { class: 'user-name' }, text(username)),
      el('span', { class: 'user-role' }, text('Administrator'))),
    logout,
  );
  logout.title = state.collapsed ? 'Log out' : '';
}

function applyCollapsed() {
  state.collapsed = !state.collapsed;
  if (state.collapsed) document.documentElement.classList.add('collapsed');
  else document.documentElement.classList.remove('collapsed');
  try { localStorage.setItem('ax.collapsed', state.collapsed ? '1' : '0'); } catch { /* ignore */ }
  toggleGlyph.textContent = state.collapsed ? '\u25B6' : '\u25C0';
  toggleEl.title = state.collapsed ? 'Expand sidebar' : 'Collapse sidebar';
}

toggleEl.addEventListener('click', () => {
  applyCollapsed();
  buildNav();
  buildFooter(state.app ? state.app.username : '');
});

async function logoutPressed() {
  const r = await call('account.logout');
  if (r.ok) window.location.reload();
}

function onHostEvent(evt) {
  if (evt === 'settings.changed') {
    refreshKeyAware();
  }
}

let refreshingKey = false;
async function refreshKeyAware() {
  if (refreshingKey) return;
  refreshingKey = true;
  try {
    const r = await call('app.state');
    if (r.ok) state.app = r.data;
    if (['settings', 'dashboard', 'generate'].includes(state.page)) await showPage(state.page, true);
  } finally {
    refreshingKey = false;
  }
}

/* ------------------------------ Router ------------------------------- */

async function showPage(name, quiet) {
  state.page = name;
  buildNav();
  if (!quiet) pageEl.scrollTop = 0;
  switch (name) {
    case 'dashboard': await renderDashboard(); break;
    case 'generate': renderGenerate(); break;
    case 'active': await renderActive(); break;
    case 'archived': await renderArchived(); break;
    case 'verify': renderVerify(); break;
    case 'settings': renderSettings(); break;
  }
}

/* ------------------------------ Banner ------------------------------- */

function keyBanner(key) {
  if (!key) return null;
  const kind = !key.configured || key.safe === false ? 'bad' : (key.warning ? 'warn' : 'good');
  const heading = !key.configured
    ? 'Signing key not configured'
    : (key.safe === false ? 'Signing is blocked' : (key.warning ? 'Signing key needs attention' : 'Signing key ready'));
  return el('div', { class: 'status-banner ' + kind },
    el('b', {}, text(heading + '. ')),
    text(key.message || ''));
}

function pillFor(row) {
  const classes = { active: 'active', expiringSoon: 'expiring', expired: 'expired', archived: 'archived' };
  return el('span', { class: 'pill ' + (classes[row.status] || 'active') }, text(row.statusLabel));
}

/* ------------------------------ Dashboard ---------------------------- */

async function renderDashboard() {
  const r = await call('app.state');
  pageEl.replaceChildren(el('h1', {}, text('Welcome back, ' + (state.app?.username || ''))));
  if (!r.ok) {
    pageEl.append(el('p', { class: 'lead' }, text('Could not load the dashboard: ' + r.error)));
    return;
  }
  state.app = r.data;
  const s = state.app;
  const todayStr = new Date().toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' });
  pageEl.append(
    el('p', { class: 'lead' }, text('Here is what is happening with your licenses today, ' + todayStr + '.')),

    el('div', { class: 'cards' },
      statCard('Active', s.counts.active, '> 30 days remaining', 'accent-active'),
      statCard('Expiring Soon', s.counts.expiringSoon, '30 days or fewer', 'accent-warning'),
      statCard('Expired', s.counts.expired, 'expiry date reached', 'accent-danger'),
      statCard('Archived', s.counts.archived, 'kept for reference', 'accent-neutral')),

    el('section', { class: 'card' },
      el('header', { class: 'card-header' },
        el('h2', { class: 'card-title' }, text('Recent Licenses')),
        el('button', { class: 'link-btn', type: 'button', onclick: () => showPage('active') }, text('View all \u2192'))),
      el('div', { class: 'card-body' }, s.recent.length === 0
        ? el('div', { class: 'empty' },
          el('span', { class: 'eicon' }, text('\uD83D\uDCCB')),
          text('No licenses recorded yet. Generate your first license to get started.'),
          el('div', { style: 'margin-top:14px' },
            el('button', { class: 'btn btn-primary', type: 'button', onclick: () => { state.generate = { step: 1, school: '', hwid: '', expires: '' }; showPage('generate'); } }, text('Generate a license')))):
        el('div', { class: 'table-wrap' },
          licensesTable(s.recent, [
            renderSchoolCol.bind(null, { showPath: false }),
            renderExpiresCol,
            renderRemainingCol,
            renderStatusCol,
            (row) => el('td', {}, text(row.createdLocal)),
          ], { heading: 'Created' })))),
  );
}

function statCard(label, value, sub, accent) {
  return el('div', { class: 'stat-card ' + accent },
    el('span', { class: 'stat-value' }, text(String(value))),
    el('span', { class: 'stat-label' }, text(label)),
    el('span', { class: 'stat-sub' }, text(sub)));
}

function licensesTable(rows, columnCells, opts = {}) {
  const head = el('tr', {},
    el('th', {}, text('School')),
    el('th', {}, text('Expiration')),
    el('th', {}, text('Remaining')),
    el('th', {}, text('Status')),
    el('th', {}, text(opts.heading)));

  return el('table', {},
    el('thead', {}, head),
    el('tbody', {}, ...rows.map((row) => el('tr', {}, ...columnCells.map((cell) => cell(row))))));
}

function renderSchoolCol(settings, row) {
  return el('td', {},
    text(row.schoolName),
    settings.showPath && row.licenseFilePath ? el('div', { class: 'hint', style: 'font-size:11px;color:var(--muted)' }, text(row.licenseFilePath)) : null);
}

function renderExpiresCol(row) {
  return el('td', { class: 'mono' }, text(row.expires));
}

function renderRemainingCol(row) {
  return el('td', {}, text(row.remainingDays === null ? '\u2014' : String(row.remainingDays)), ' ', el('span', { style: 'color:var(--muted);font-size:12px' }, text('(' + row.daysLabel + ')')));
}

function renderStatusCol(row) {
  return el('td', {}, pillFor(row));
}

/* ------------------------------ Generate ----------------------------- */

function renderGenerate() {
  pageEl.replaceChildren(el('h1', {}, text('Generate License')),
    el('p', { class: 'lead' }, text('Create a signed, byte-compatible license for a school. Review before signing; signing happens only on \u201CSign & Save\u201D.')));

  if (state.generate.step === 2) { renderGenerateReview(); return; }
  if (state.generate.step === 3) { renderGenerateSuccess(); return; }
  renderGenerateForm();
}

function renderGenerateForm() {
  const kb = keyBanner(state.app && state.app.key);
  const school = tsInput(state.generate.school, { maxlength: '150', placeholder: 'E.g. Riverside High School' });
  const hwid = tsInput(state.generate.hwid, { placeholder: 'Motherboard serial, MAC, or other hardware fingerprint', class: 'input mono' });
  hwid.classList.add('input');
  const expires = el('input', { type: 'date', class: 'input', min: todayStr() });

  const errorBox = el('div', { class: 'form-error' });
  const reviewBtn = el('button', { class: 'btn btn-primary', type: 'button' }, text('Review license'));

  reviewBtn.onclick = async () => {
    errorBox.textContent = '';
    reviewBtn.disabled = true;
    const r = await call('generate.review', {
      school: school.value,
      hwid: hwid.value,
      expires: expires.value,
    });
    reviewBtn.disabled = false;
    if (!r.ok) { errorBox.textContent = r.error; return; }
    state.generate = {
      step: 2,
      school: school.value,
      hwid: hwid.value,
      expires: expires.value,
      review: r.data,
    };
    renderGenerate();
  };

  pageEl.append(
    kb || el('div', {}),
    el('div', { class: 'card' },
      el('header', { class: 'card-header' }, el('h2', { class: 'card-title' }, text('License details'))),
      el('div', { class: 'card-body' },
        el('div', { class: 'field' },
          el('label', {}, text('School name')),
          school,
          el('span', { class: 'hint' }, text('150 characters or fewer.'))),
        el('div', { class: 'field' },
          el('label', {}, text('Hardware ID')),
          hwid,
          el('span', { class: 'hint' }, text('Normalized to uppercase alphanumerics only (A\u2013Z, 0\u20139).'))),
        el('div', { class: 'field' },
          el('label', {}, text('Expiration date')),
          expires,
          el('span', { class: 'hint' }, text('The license stops being valid on this date at midnight.'))),
        errorBox,
        el('div', { class: 'form-actions' },
          reviewBtn))));
}

function renderGenerateReview() {
  const g = state.generate;
  const data = g.review;
  const kb = keyBanner(data && data.key);

  pageEl.append(
    kb || el('div', {}),
    el('div', { class: 'card' },
      el('header', { class: 'card-header' }, el('h2', { class: 'card-title' }, text('Review before signing'))),
      el('div', { class: 'card-body' },
        el('div', { class: 'review-grid' },
          el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('School')), el('div', { class: 'v' }, text(data.school))),
          el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Hardware ID (normalized)')), el('div', { class: 'v' }, text(data.normalizedHwid))),
          el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Expiration')), el('div', { class: 'v' }, text(data.expires))),
          el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Validity')), el('div', { class: 'v' }, text(data.remainingDays + ' days (' + data.daysLabel + ')'))),
        el('div', { class: 'k', style: 'margin-bottom:6px;font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.5px' }, text('Signed payload (exact, byte-compatible)')),
        el('pre', { class: 'payload-preview' }, text(data.payloadPreview)),
        el('div', { class: 'form-actions' },
          el('button', { class: 'btn btn-secondary', type: 'button', onclick: () => { state.generate.step = 1; renderGenerate(); } }, text('\u2190 Back')),
          el('button', { class: 'btn btn-primary', type: 'button', onclick: signLicense }, text('Sign & Save \u2026')))))));
}

const signError = el('div', { class: 'form-error' });

async function signLicense() {
  const btn = event.target;
  btn.disabled = true;
  signError.textContent = '';
  const g = state.generate;
  const r = await call('generate.sign', { school: g.school, hwid: g.hwid, expires: g.expires });
  btn.disabled = false;
  if (!r.ok) { signError.textContent = r.error; return; }
  state.generate = { step: 3, school: g.school, hwid: g.hwid, expires: g.expires, savedPath: r.data.path };
  renderGenerate();
}

function renderGenerateSuccess() {
  const g = state.generate;
  pageEl.append(
    el('div', { class: 'success-banner' },
      el('span', { class: 'big' }, text('\u2714 License signed and saved')),
      text('The license was signed with the production key and saved.'),
      el('div', { class: 'success-path' }, text(g.savedPath))),
    el('div', { class: 'form-actions' },
      el('button', { class: 'btn btn-secondary', type: 'button', onclick: () => call('shell.openLocation', { path: g.savedPath }) }, text('Open folder')),
      el('button', { class: 'btn btn-secondary', type: 'button', onclick: () => { state.generate = { step: 1, school: '', hwid: '', expires: '' }; renderGenerate(); } }, text('Generate another')),
      el('button', { class: 'btn btn-primary', type: 'button', onclick: () => showPage('active') }, text('View Active Licenses'))));
}

/* -------------------------- Active licenses -------------------------- */

let activeRows = [];
async function renderActive() {
  const r = await call('licenses.list', { archived: false });
  if (!r.ok) { pageEl.replaceChildren(el('h1', {}, text('Active Licenses')), el('p', { class: 'lead' }, text(r.error))); return; }
  activeRows = r.data;
  renderActiveTable();
}

function renderActiveTable() {
  const q = state.activeSearch.trim().toLowerCase();
  const rows = activeRows
    .filter((row) => !q || row.schoolName.toLowerCase().includes(q) || row.hardwareId.toLowerCase().includes(q))
    .slice()
    .sort((a, b) => {
      const key = state.activeSort.key;
      let av = a[key], bv = b[key];
      if (key === 'remainingDays') { av = av === null ? -Infinity : av; bv = bv === null ? -Infinity : bv; }
      if (typeof av === 'string') { av = av.toLocaleLowerCase(); bv = String(bv).toLocaleLowerCase(); }
      const cmp = av < bv ? -1 : av > bv ? 1 : 0;
      return cmp * state.activeSort.dir;
    });

  const search = el('input', {
    class: 'input search-input',
    type: 'search',
    placeholder: 'Search school or HWID \u2026',
    value: state.activeSearch,
    oninput: () => { state.activeSearch = search.value; renderActiveTable(); },
  });

  const sortBy = (key) => {
    state.activeSort = { key, dir: state.activeSort.key === key ? -state.activeSort.dir : 1 };
    renderActiveTable();
  };

  const head = el('tr', {},
    sortTh('School', 'schoolName'),
    sortTh('Expiration', 'expires'),
    sortTh('Remaining', 'remainingDays'),
    sortTh('Status', 'status'),
    sortTh('Created', 'createdLocal'),
    el('th', {}, text('Actions')));

  function sortTh(label, key) {
    return el('th', { class: 'sortable', onclick: () => sortBy(key) }, text(label),
      el('span', { class: 'sort-ind' }, text(state.activeSort.key === key ? (state.activeSort.dir === 1 ? '\u25B2' : '\u25BC') : '')));
  }

  const tbody = el('tbody', {});
  if (rows.length === 0) {
    tbody.append(el('tr', {}, el('td', { colspan: '6', class: 'empty' }, text('No active licenses' + (q ? ' matching \u201C' + state.activeSearch + '\u201D' : '') + '.'))));
  } else {
    for (const row of rows) {
      tbody.append(el('tr', {},
        el('td', {}, text(row.schoolName)),
        el('td', { class: 'mono' }, text(row.expires)),
        el('td', {}, text(String(row.remainingDays ?? '\u2014')), ' ', el('span', { style: 'color:var(--muted);font-size:12px' }, text('(' + row.daysLabel + ')'))),
        el('td', {}, pillFor(row)),
        el('td', {}, text(row.createdLocal)),
        el('td', {},
          el('div', { class: 'row-actions' },
            el('button', { class: 'link-btn', type: 'button', onclick: () => showDetails(row) }, text('Details')),
            row.licenseFilePath ? el('button', { class: 'link-btn', type: 'button', onclick: () => verifyRecord(row) }, text('Verify')) : null,
            el('button', { class: 'link-btn', type: 'button', onclick: () => archiveRow(row) }, text('Archive'))))));
    }
  }

  pageEl.replaceChildren(
    el('h1', {}, text('Active Licenses')),
    el('p', { class: 'lead' }, text('' + rows.length + ' of ' + activeRows.length + ' non-archived licenses.')),
    el('section', { class: 'card' },
      el('div', { class: 'card-body' },
        el('div', { class: 'toolbar' }, search,
          el('button', { class: 'btn btn-primary', type: 'button', onclick: () => { state.generate = { step: 1, school: '', hwid: '', expires: '' }; showPage('generate'); } }, text('+ Generate license'))),
        el('div', { class: 'table-wrap' }, el('table', {}, el('thead', {}, head), tbody)))));
}

async function archiveRow(row) {
  if (!(await confirmModal('Archive \"' + row.schoolName + '\"? It will move to Archived Licenses and remain recoverable.', 'Archive'))) return;
  const r = await call('licenses.archive', { id: row.id });
  if (!r.ok) { toast(r.error, 'error'); return; }
  toast('License archived.');
  renderActive();
}

/* ------------------------- Archived licenses ------------------------- */

let archivedRows = [];
async function renderArchived() {
  const r = await call('licenses.list', { archived: true });
  if (!r.ok) { pageEl.replaceChildren(el('h1', {}, text('Archived Licenses')), el('p', { class: 'lead' }, text(r.error))); return; }
  archivedRows = r.data;
  renderArchivedTable();
}

function renderArchivedTable() {
  const q = state.archivedSearch.trim().toLowerCase();
  const rows = archivedRows
    .filter((row) => !q || row.schoolName.toLowerCase().includes(q) || row.hardwareId.toLowerCase().includes(q))
    .slice()
    .sort((a, b) => {
      let av = a[state.archivedSort.key], bv = b[state.archivedSort.key];
      if (typeof av === 'string') { av = av.toLowerCase(); bv = String(bv).toLowerCase(); }
      const cmp = av < bv ? -1 : av > bv ? 1 : 0;
      return cmp * state.archivedSort.dir;
    });

  const search = el('input', {
    class: 'input search-input',
    type: 'search',
    placeholder: 'Search school or HWID \u2026',
    value: state.archivedSearch,
    oninput: () => { state.archivedSearch = search.value; renderArchivedTable(); },
  });

  pageEl.replaceChildren(
    el('h1', {}, text('Archived Licenses')),
    el('p', { class: 'lead' }, text('Archived licenses are kept for reference and can be restored at any time.')),
    el('section', { class: 'card' },
      el('div', { class: 'card-body' },
        el('div', { class: 'toolbar' }, search),
        el('div', { class: 'table-wrap' },
          el('table', {},
            el('thead', {}, el('tr', {},
              el('th', {}, text('School')),
              el('th', {}, text('Expiration')),
              el('th', {}, text('Status')),
              el('th', {}, text('Archived')),
              el('th', {}, text('Actions')))),
            el('tbody', {}, rows.length === 0
              ? el('tr', {}, el('td', { colspan: '5', class: 'empty' },
                el('span', { class: 'eicon' }, text('\uD83D\uDDC4')),
                text(q ? 'Nothing matches your search.' : 'Nothing archived yet.')))
              : rows.map((row) => el('tr', {},
                el('td', {}, text(row.schoolName)),
                el('td', { class: 'mono' }, text(row.expires)),
                el('td', {}, pillFor(row)),
                el('td', { class: 'mono' }, text(row.isArchived ? row.createdLocal : '\u2014')),
                el('td', {},
                  el('div', { class: 'row-actions' },
                    el('button', { class: 'link-btn', type: 'button', onclick: () => restoreRow(row) }, text('Restore')),
                    el('button', { class: 'link-btn danger', type: 'button', onclick: () => deleteRow(row) }, text('Delete permanently'))))))))))),
  );
}

async function restoreRow(row) {
  const r = await call('licenses.restore', { id: row.id });
  if (!r.ok) { toast(r.error, 'error'); return; }
  toast('License restored to Active.');
  renderArchived();
}

async function deleteRow(row) {
  if (!(await confirmModal('Permanently delete the record for \"' + row.schoolName + '\"? The saved license.lic file is not touched, but this action cannot be undone.', 'Delete permanently'))) return;
  const r = await call('licenses.delete', { id: row.id });
  if (!r.ok) { toast(r.error, 'error'); return; }
  toast('Record deleted.');
  renderArchived();
}

/* ------------------------------- Verify ------------------------------ */

let verifyResult = null;

function renderVerify() {
  const fileInput = el('input', { class: 'input', style: 'flex:1 1 380px;min-width:200px', placeholder: 'No file selected', readonly: 'readonly' });
  const hwidInput = el('input', { class: 'input', placeholder: 'Optional: machine HWID to compare', style: 'flex:1 1 260px;min-width:180px' });

  const chooseBtn = el('button', { class: 'btn btn-secondary', type: 'button' }, text('Choose file \u2026'));
  const runBtn = el('button', { class: 'btn btn-primary', type: 'button', disabled: true }, text('Verify'));

  chooseBtn.onclick = async () => {
    const r = await call('verify.open');
    if (r.ok && r.data.path) {
      fileInput.value = r.data.path;
      runBtn.disabled = false;
      verifyResult = null;
      renderResultSlot();
    }
  };

  runBtn.onclick = async () => {
    runBtn.disabled = true;
    const r = await call('verify.run', { path: fileInput.value, expectedHwid: hwidInput.value });
    runBtn.disabled = false;
    verifyResult = r.ok ? r.data : { valid: false, error: r.error };
    renderResultSlot();
  };

  const resultSlot = el('div', { id: 'verifyResult' });

  pageEl.replaceChildren(
    el('h1', {}, text('Verify License')),
    el('p', { class: 'lead' }, text('Confirm a license.lic file against the Axumera production public key. Signature checks run on the C# side.')),
    el('section', { class: 'card' },
      el('header', { class: 'card-header' }, el('h2', { class: 'card-title' }, text('License file'))),
      el('div', { class: 'card-body' },
        el('div', { class: 'settings-row', style: 'margin-bottom:12px' }, fileInput, chooseBtn),
        el('div', { class: 'field' }, hwidInput, el('span', { class: 'hint' }, text('Optional. Comparison is done after HWID normalization.'))),
        el('div', { class: 'form-actions' }, runBtn))),
    resultSlot);
}

function renderResultSlot() {
  const slot = document.getElementById('verifyResult');
  if (!slot) return;
  if (!verifyResult) { slot.replaceChildren(); return; }
  const v = verifyResult;
  const box = el('div', { class: 'verify-result ' + (v.valid ? 'valid' : 'invalid') });
  if (v.valid) {
    box.append(
      el('span', { class: 'big' }, text('\u2714 Signature valid')),
      el('p', { style: 'margin:0 0 8px' }, text('This file was signed by the Axumera private key.')),
      el('table', { class: 'settings-kv', style: 'width:100%;margin-top:6px' },
        el('tbody', {},
          kvRow('School', v.schoolName),
          kvRow('Hardware ID', v.hardwareId),
          kvRow('Expires', v.expires),
          kvRow('Status', v.expired ? 'Expired (after ' + v.expires + ')' : 'Valid'),
          kvRow('Machine matches', v.machineMatches === null ? 'Not compared' : (v.machineMatches ? 'Yes' : 'No')))));
  } else {
    box.append(
      el('span', { class: 'big' }, text('\u2716 Invalid license')),
      v.error ? el('p', { style: 'margin:6px 0 0' }, text(v.error)) : null);
  }
  slot.replaceChildren(box);
}

function kvRow(k, val) {
  return el('tr', {}, el('td', { style: 'padding:4px 12px 4px 0;color:var(--muted);width:140px' }, text(k)), el('td', {}, text(val || '\u2014')));
}

/* ------------------------------- Settings ----------------------------- */

function renderSettings() {
  const s = state.app;
  const key = s ? s.key : null;
  const kb = keyBanner(key);

  pageEl.replaceChildren(
    el('h1', {}, text('Settings')),
    el('p', { class: 'lead' }, text('Account, session, signing key and application details.')),

    el('div', { class: 'settings-group' },
      el('h3', {}, text('Account')),
      el('p', { class: 'settings-kv' }, text('Username: '), el('b', {}, text(s ? s.username : ''))),
      el('div', { class: 'form-actions', style: 'margin-top:10px' },
        el('button', { class: 'btn btn-secondary', type: 'button', onclick: openChangePassword }, text('Change password')))),
    el('hr', { style: 'border:0;border-top:1px solid var(--border);margin:18px 0 26px' }),

    el('div', { class: 'settings-group' },
      el('h3', {}, text('Signing key')),
      el('p', { class: 'desc' }, text('The private key file that signs licenses. Only its path is stored here; the key itself never leaves your machine.')),
      kb || el('div', {}),
      el('div', { class: 'settings-row' },
        el('input', { class: 'input', id: 'keyPath', placeholder: 'No key selected', value: key && key.path || '', readonly: 'readonly' }),
        el('button', { class: 'btn btn-secondary keyBrowse', type: 'button' }, text('Browse \u2026')),
        el('button', { class: 'btn btn-danger keyRestrict', type: 'button' }, text('Restrict access')),
        el('button', { class: 'btn btn-primary keySave', type: 'button' }, text('Save key')))),
    el('hr', { style: 'border:0;border-top:1px solid var(--border);margin:18px 0 26px' }),

    el('div', { class: 'settings-group' },
      el('h3', {}, text('Session')),
      el('p', { class: 'desc' }, text('Sign out and return to the login screen.')),
      el('div', { class: 'form-actions' },
        el('button', { class: 'btn btn-secondary', type: 'button', onclick: logoutPressed }, text('Log out')))),
    el('hr', { style: 'border:0;border-top:1px solid var(--border);margin:18px 0 26px' }),

    el('div', { class: 'settings-group' },
      el('h3', {}, text('Application')),
      el('p', { class: 'settings-kv' }, text('Version: '), el('b', {}, text(s ? s.version : ''))),
      el('p', { class: 'settings-kv' }, text('License format: '), el('b', {}, text(s ? s.licenseFormatVersion : ''))),
      el('p', { class: 'settings-kv' }, text('Product: '), text(s ? s.productName : ''))),
  );

  const path = document.getElementById('keyPath');
  document.querySelector('.keyBrowse').onclick = async () => {
    const r = await call('key.browse');
    if (r.ok && r.data.path) path.value = r.data.path;
  };
  document.querySelector('.keySave').onclick = async () => {
    const r = await call('key.save', { path: path.value });
    if (!r.ok) { toast(r.error, 'error'); return; }
    toast(r.data.message);
  };
  document.querySelector('.keyRestrict').onclick = async () => {
    if (!(await confirmModal('Restrict this signing-key file so only your account, Administrators and SYSTEM can access it?', 'Restrict'))) return;
    const r = await call('key.restrict', { path: path.value });
    if (!r.ok) { toast(r.error, 'error'); return; }
    toast(r.data.message);
  };
}

/* --------------------------- Change password -------------------------- */

function openChangePassword() {
  const current = el('input', { type: 'password', class: 'input', autocomplete: 'current-password' });
  const next = el('input', { type: 'password', class: 'input', autocomplete: 'new-password' });
  const confirm = el('input', { type: 'password', class: 'input', autocomplete: 'new-password' });
  const err = el('div', { class: 'form-error' });

  const save = el('button', { class: 'btn btn-primary', type: 'button' }, text('Save new password'));
  save.onclick = async () => {
    err.textContent = '';
    if (next.value.length < 8) { err.textContent = 'Password must be at least 8 characters.'; return; }
    if (next.value !== confirm.value) { err.textContent = 'New passwords do not match.'; return; }
    save.disabled = true;
    const r = await call('account.changePassword', { current: current.value, new: next.value, confirm: confirm.value });
    save.disabled = false;
    if (!r.ok) { err.textContent = r.error; return; }
    closeModal();
    toast('Password updated.');
  };

  openModal('Change password', el('div', {},
    fieldBox('Current password', current),
    fieldBox('New password (8+ characters)', next),
    fieldBox('Confirm new password', confirm),
    el('div', { class: 'form-error' }, err.textContent === '' ? '' : err.textContent),
  ), [el('button', { class: 'btn btn-secondary', type: 'button', onclick: closeModal }, text('Cancel')), save]);
}

function fieldBox(labelText, input) {
  return el('div', { class: 'field' },
    el('label', {}, text(labelText)),
    input);
}

/* --------------------------- Record details --------------------------- */

async function showDetails(row) {
  const r = await call('licenses.get', { id: row.id });
  if (!r.ok) { toast(r.error, 'error'); return; }
  const d = r.data.record;
  const v = r.data.verification;

  const body = el('div', {},
    el('div', { class: 'review-grid' },
      el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('School')), el('div', { class: 'v' }, text(d.schoolName))),
      el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Hardware ID')), el('div', { class: 'v' }, text(d.hardwareId))),
      el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Expiration')), el('div', { class: 'v' }, text(d.expires + ' \u00B7 ' + d.daysLabel))),
      el('div', { class: 'review-item' }, el('div', { class: 'k' }, text('Created')), el('div', { class: 'v' }, text(d.createdLocal)))),
    el('p', { class: 'settings-kv', style: 'margin:14px 0 0' }, text('Saved file: '), el('b', {}, text(d.licenseFilePath || '\u2014'))),
    v && el('div', { style: 'margin-top:14px' },
      v.valid
        ? el('div', { class: 'verify-result valid' }, el('span', { class: 'big' }, text('\u2714 Signature valid')))
        : el('div', { class: 'verify-result invalid' }, el('span', { class: 'big' }, text('\u2716 Verification failed')), el('p', { style: 'margin:6px 0 0' }, text(v.error || '')))));

  openModal('License details', body, [
    el('button', { class: 'btn btn-secondary', type: 'button', onclick: closeModal }, text('Close')),
    d.licenseFilePath ? el('button', { class: 'btn btn-secondary', type: 'button', onclick: () => call('shell.openLocation', { path: d.licenseFilePath }) }, text('Open folder')) : null,
  ]);
}

async function verifyRecord(row) {
  const r = await call('verify.run', { path: row.licenseFilePath, expectedHwid: row.hardwareId });
  const v = r.ok ? r.data : { valid: false, error: r.error };
  const body = el('div', {},
    v.valid
      ? el('div', { class: 'verify-result valid' },
        el('span', { class: 'big' }, text('\u2714 Signature valid')),
        el('p', { style: 'margin:8px 0 0' }, text('School: ' + v.schoolName)),
        el('p', { style: 'margin:4px 0 0' }, text('Expires: ' + v.expires + (v.expired ? ' (expired)' : ''))),
        el('p', { style: 'margin:4px 0 0' }, text('Machine matches HWID: ' + (v.machineMatches === null ? 'not compared' : (v.machineMatches ? 'yes' : 'no')))))
      : el('div', { class: 'verify-result invalid' },
        el('span', { class: 'big' }, text('\u2716 Verification failed')),
        el('p', { style: 'margin:8px 0 0' }, text(v.error || 'Unknown error.'))));
  openModal('Verify \"' + row.schoolName + '\"', body, [el('button', { class: 'btn btn-secondary', type: 'button', onclick: closeModal }, text('Close'))]);
}

/* ------------------------------- Modals ------------------------------- */

function openModal(title, body, actions) {
  modalRoot.replaceChildren(
    el('div', { class: 'modal', role: 'dialog', 'aria-label': title },
      el('header', { class: 'modal-header' },
        el('h2', { class: 'modal-title' }, text(title)),
        el('button', { class: 'modal-close', type: 'button', 'aria-label': 'Close', onclick: closeModal }, text('\u00D7'))),
      el('div', { class: 'modal-body' }, body),
      el('div', { class: 'modal-actions' }, ...(actions || []))));
  modalRoot.hidden = false;
  modalRoot.classList.add('open');
  const firstInput = modalRoot.querySelector('input');
  if (firstInput) firstInput.focus();
}

function closeModal() {
  modalRoot.classList.remove('open');
  modalRoot.hidden = true;
  modalRoot.replaceChildren();
}

function confirmModal(message, okLabel) {
  return new Promise((resolve) => {
    let done = false;
    const finish = (result) => {
      if (done) return;
      done = true;
      closeModal();
      resolve(result);
    };
    const confirm = el('button', { class: 'btn btn-danger', type: 'button', onclick: () => finish(true) }, text(okLabel));
    const cancel = el('button', { class: 'btn btn-secondary', type: 'button', onclick: () => finish(false) }, text('Cancel'));
    modalRoot.replaceChildren(
      el('div', { class: 'modal', role: 'alertdialog', 'aria-label': 'Confirm' },
        el('header', { class: 'modal-header' }, el('h2', { class: 'modal-title' }, text('Please confirm'))),
        el('div', { class: 'modal-body' }, el('p', { style: 'margin:0' }, text(message))),
        el('div', { class: 'modal-actions' }, cancel, confirm)));
    modalRoot.hidden = false;
    modalRoot.classList.add('open');
    confirm.focus();
  });
}

modalRoot.addEventListener('click', (e) => { if (e.target === modalRoot && modalRoot.classList.contains('open')) closeModal(); });

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && modalRoot.classList.contains('open')) closeModal();
});

/* -------------------------------- Toasts ------------------------------ */

function toast(message, kind = 'success') {
  const node = el('div', { class: 'toast ' + kind }, text(message));
  toastsEl.append(node);
  setTimeout(() => node.remove(), 4000);
}

/* -------------------------------- Init -------------------------------- */

function todayStr() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
}

(async function init() {
  const sess = await call('account.session');
  if (!sess.ok || !sess.data || !sess.data.authenticated) {
    document.body.classList.add('locked');
    pageEl.replaceChildren(
      el('h1', {}, text('Access locked')),
      el('p', { class: 'lead' }, text('This session is not authenticated. Sign in and restart the application to continue.')));
    return;
  }
  const r = await call('app.state');
  if (!r.ok) {
    pageEl.replaceChildren(el('h1', {}, text('Axumera License Manager')),
      el('p', { class: 'lead' }, text(r.error)));
    return;
  }
  state.app = r.data;
  buildNav();
  buildFooter(r.data.username);
  applyCollapsedStateForPaint();
  showPage('dashboard');
})();

function applyCollapsedStateForPaint() {
  // Sync the toggle glyph/title with the class applied by the head script so
  // they stay consistent even before the first user interaction.
  toggleGlyph.textContent = state.collapsed ? '\u25B6' : '\u25C0';
  toggleEl.title = state.collapsed ? 'Expand sidebar' : 'Collapse sidebar';
}