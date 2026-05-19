// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// gamekit-admin.js — Phase 03.1 client-side interactivity bundle.
//
// Owns:
//   * Cmd+K / Ctrl+K command-palette open/close (state in [data-palette-open] on <html>)
//   * Cmd+\ / Ctrl+\ sidebar collapse toggle    ([data-sidebar] on <html>)
//   * Esc closes palette and Tweaks panel; restores focus to opener
//   * Tweaks panel state read/write/apply against localStorage 'gamekit.admin.tweaks'
//   * Audit row expand toggle (aria-expanded)
//   * Clipboard write with Safari/exec-command fallback
//   * Bridge to Blazor via window.GKAdmin._dotNetRef registered by MainLayout
//
// Loaded via <script src="_content/GameKit.Admin.UI/gamekit-admin.js" defer> from App.razor.
// Same-origin -> CSP `script-src 'self'` covers it; NO nonce attribute needed (RESEARCH SP-9).
//
// Discipline: NEVER set HTML from a string — only textContent for any user-supplied
// string (player display name, audit reason). The grep verification rule is that the
// literal token used by the unsafe DOM API must not appear anywhere in this file (the
// safe alternative is to compose with createElement + appendChild + textContent).
// NEVER fetch external scripts. NEVER auto-launch dialogs without going through the
// registered DotNetObjectReference (registerDialogBridge).

(function () {
  'use strict';

  // -------- Constants ----------------------------------------------------------
  var TWEAK_KEY = 'gamekit.admin.tweaks';
  var DEFAULTS  = {
    accent:   'violet',
    density:  'compact',
    sidebar:  'expanded',
    banLoud:  'medium',
    dashDir:  'D'
  };
  var ATTR_MAP = {
    accent:  'data-accent',
    density: 'data-density',
    sidebar: 'data-sidebar',
    banLoud: 'data-ban-loud',
    dashDir: 'data-dashboard-dir'
  };

  // -------- Tweaks I/O ---------------------------------------------------------
  function loadTweaks() {
    try {
      var raw = localStorage.getItem(TWEAK_KEY);
      var t   = raw ? JSON.parse(raw) : {};
      var out = {};
      Object.keys(DEFAULTS).forEach(function (k) { out[k] = t[k] || DEFAULTS[k]; });
      return out;
    } catch (e) {
      return Object.assign({}, DEFAULTS);
    }
  }

  function saveTweaks(patch) {
    var current = loadTweaks();
    Object.keys(patch || {}).forEach(function (k) {
      if (Object.prototype.hasOwnProperty.call(DEFAULTS, k)) current[k] = patch[k];
    });
    try { localStorage.setItem(TWEAK_KEY, JSON.stringify(current)); } catch (e) { /* private mode */ }
    applyAttrs(current);
  }

  function resetTweaks() {
    try { localStorage.removeItem(TWEAK_KEY); } catch (e) { /* private mode */ }
    applyAttrs(DEFAULTS);
  }

  function applyAttrs(t) {
    var html = document.documentElement;
    Object.keys(ATTR_MAP).forEach(function (k) {
      html.setAttribute(ATTR_MAP[k], t[k] || DEFAULTS[k]);
    });
    // Phase 03.1-11 gap closure (WARNING-01): reflect the active option in each Tweaks
    // radiogroup so the existing CSS rule `.tweaks-options button[aria-checked='true']`
    // can style it AND so screen readers announce the selection. Iterates every
    // [data-tweak][data-value] button in the document and toggles aria-checked based on
    // the current tweak value. Idempotent — safe to call on every saveTweaks roundtrip.
    var optionButtons = document.querySelectorAll('button[data-tweak][data-value]');
    for (var i = 0; i < optionButtons.length; i++) {
      var ob  = optionButtons[i];
      var k   = ob.getAttribute('data-tweak');
      var v   = ob.getAttribute('data-value');
      var cur = t[k] || DEFAULTS[k];
      ob.setAttribute('aria-checked', cur === v ? 'true' : 'false');
    }
  }

  // -------- Palette / Tweaks open/close ---------------------------------------
  var _paletteOpener = null;
  var _tweaksOpener  = null;

  function openPalette() {
    _paletteOpener = document.activeElement;
    document.documentElement.setAttribute('data-palette-open', 'true');
    var input = document.querySelector('.palette-input input, .palette input');
    if (input) input.focus();
  }
  function closePalette() {
    document.documentElement.removeAttribute('data-palette-open');
    if (_selectedAction !== null) {
      _selectedAction = null;
      document.removeEventListener('input', _onTargetSearchInput);
      var input = document.querySelector('.palette-input input');
      if (input) {
        input.value = '';
        input.placeholder = 'Type a command…';
      }
    }
    if (_paletteOpener && _paletteOpener.focus) _paletteOpener.focus();
    _paletteOpener = null;
  }
  function openTweaks() {
    _tweaksOpener = document.activeElement;
    document.documentElement.setAttribute('data-tweaks-open', 'true');
    // Refresh aria-checked on the radio buttons now that the panel (and its buttons)
    // are visible. The deferred script's initial applyAttrs() call ran before the
    // Blazor circuit mounted TweaksPanel, so the button-level reflection found 0
    // nodes — running it again here guarantees aria-checked is correct before the
    // operator sees the panel. Idempotent + safe; the <html>-attribute writes inside
    // applyAttrs are a no-op when the values haven't changed.
    applyAttrs(loadTweaks());
  }
  function closeTweaks() {
    document.documentElement.removeAttribute('data-tweaks-open');
    if (_tweaksOpener && _tweaksOpener.focus) _tweaksOpener.focus();
    _tweaksOpener = null;
  }

  // -------- Sidebar -----------------------------------------------------------
  function toggleSidebar() {
    var html = document.documentElement;
    var next = html.getAttribute('data-sidebar') === 'collapsed' ? 'expanded' : 'collapsed';
    html.setAttribute('data-sidebar', next);
    saveTweaks({ sidebar: next });
  }

  // -------- Audit row expand --------------------------------------------------
  function toggleAuditRow(rowId) {
    var btn = document.querySelector('button[data-audit-row="' + rowId + '"]');
    if (!btn) return;
    var isOpen = btn.getAttribute('aria-expanded') === 'true';
    btn.setAttribute('aria-expanded', isOpen ? 'false' : 'true');
    var body = document.querySelector('[data-audit-body="' + rowId + '"]');
    if (body) body.hidden = isOpen;
  }

  // -------- Clipboard with Safari/old-browser fallback ------------------------
  function copyClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text)
        .then(function () { return true; })
        .catch(function () { return _execCommandFallback(text); });
    }
    return Promise.resolve(_execCommandFallback(text));
  }
  function _execCommandFallback(text) {
    try {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'absolute';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      var ok = document.execCommand('copy');
      document.body.removeChild(ta);
      return ok;
    } catch (e) { return false; }
  }

  // -------- Blazor bridge (Plan 04 dispatches into MainLayout.OpenDialog) -----
  var _dotNetRef = null;
  function registerDialogBridge(ref) { _dotNetRef = ref; }
  function invokeDialog(commandId, targetId, targetName) {
    if (!_dotNetRef) return Promise.resolve(false);
    return _dotNetRef.invokeMethodAsync('OpenDialog', commandId, targetId, targetName)
      .then(function () { return true; })
      .catch(function () { return false; });
  }

  // -------- Tweaks panel click handler (delegated; survives re-render) --------
  document.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest && e.target.closest('[data-tweak]');
    if (btn) {
      var key = btn.getAttribute('data-tweak');
      var val = btn.getAttribute('data-value');
      if (key && val) {
        var patch = {};
        patch[key] = val;
        saveTweaks(patch);
      }
      return;
    }
    var resetBtn = e.target && e.target.closest && e.target.closest('[data-tweak-action="reset"]');
    if (resetBtn) { resetTweaks(); return; }
    // Phase 03.1-11 gap closure (BLOCKER-01): delegated handler for the × close button.
    // The Tweaks markup carries data-tweaks-action="close"; convert that to a closeTweaks()
    // call without an inline onclick= (CSP `script-src 'self' 'nonce-{n}'` would block it).
    var closeBtn = e.target && e.target.closest && e.target.closest('[data-tweaks-action="close"]');
    if (closeBtn) { closeTweaks(); return; }
    if (e.target === document.querySelector('.palette-scrim')) closePalette();
    if (e.target === document.querySelector('.tweaks-scrim'))  closeTweaks();
  });

  // -------- Palette filter -------------------------------------------------
  function _filterPalette(query) {
    var q = (query || '').trim().toLowerCase();
    var list = document.querySelector('.palette-list');
    if (!list) return;
    var rows = list.querySelectorAll('button.palette-row');
    var sectionVisible = {};
    rows.forEach(function (row) {
      var label = (row.getAttribute('data-label') || row.textContent || '').toLowerCase();
      var match = !q || label.indexOf(q) !== -1;
      row.hidden = !match;
      var section = row.previousElementSibling;
      while (section && !section.classList.contains('palette-section')) {
        section = section.previousElementSibling;
      }
      if (section) {
        var key = section.textContent || '';
        sectionVisible[key] = sectionVisible[key] || match;
      }
    });
    list.querySelectorAll('.palette-section').forEach(function (sec) {
      var key = sec.textContent || '';
      sec.hidden = !(sectionVisible[key] === true);
    });
    _resetSelection();
  }

  document.addEventListener('input', function (e) {
    if (!e.target || !e.target.matches) return;
    if (e.target.matches('.palette-input input')) _filterPalette(e.target.value);
  });

  // -------- Palette ARIA selection cycling ---------------------------------
  function _visibleRows() {
    var list = document.querySelector('.palette-list');
    if (!list) return [];
    return Array.prototype.filter.call(
      list.querySelectorAll('button.palette-row'),
      function (r) { return !r.hidden; });
  }
  function _resetSelection() {
    var rows = _visibleRows();
    rows.forEach(function (r) { r.setAttribute('aria-selected', 'false'); });
    if (rows.length > 0) rows[0].setAttribute('aria-selected', 'true');
  }
  function _moveSelection(delta) {
    var rows = _visibleRows();
    if (rows.length === 0) return;
    var idx = rows.findIndex(function (r) { return r.getAttribute('aria-selected') === 'true'; });
    if (idx < 0) idx = 0;
    var next = (idx + delta + rows.length) % rows.length;
    rows.forEach(function (r) { r.setAttribute('aria-selected', 'false'); });
    rows[next].setAttribute('aria-selected', 'true');
    rows[next].scrollIntoView({ block: 'nearest' });
  }
  function _activateSelection() {
    var rows = _visibleRows();
    var sel = rows.find(function (r) { return r.getAttribute('aria-selected') === 'true'; });
    if (sel) sel.click();
  }

  // -------- Two-step target search (D-10) ----------------------------------
  var _selectedAction = null;  // { commandId, label } when target-pick state is active

  document.addEventListener('click', function (e) {
    if (!e.target || !e.target.closest) return;
    var row = e.target.closest('button.palette-row');
    if (!row) return;
    var commandId = row.getAttribute('data-command-id');
    var requiresTarget = row.getAttribute('data-requires-target') === 'true';
    var label = row.getAttribute('data-label') || row.textContent || '';
    if (!commandId) return;

    // Target-pick rows live in the subview and carry data-target-id; dispatch the dialog.
    var targetId = row.getAttribute('data-target-id');
    if (targetId) {
      var targetName = row.getAttribute('data-display-name') || '';
      _dispatchOpenDialog(_selectedAction ? _selectedAction.commandId : commandId, targetId, targetName);
      _selectedAction = null;
      closePalette();
      return;
    }

    // Phase 03.1-10 gap closure (BLOCKER-04): nav.* rows route via window.location.href
    // to the data-url emitted by the server-side CommandPalette markup. The URL is
    // server-trusted (sourced from AdminCommandRegistry, never operator input) — safe to
    // assign directly without escaping.
    if (commandId.indexOf('nav.') === 0) {
      var url = row.getAttribute('data-url');
      closePalette();
      if (url) { window.location.href = url; }
      return;
    }

    // Action row clicks: action-without-target dispatches immediately;
    // action-with-target enters target-pick state.
    if (requiresTarget) {
      _selectedAction = { commandId: commandId, label: label };
      _enterTargetPick(label);
    } else {
      // Phase 03.1-10 gap closure (BLOCKER-03): pass Guid.Empty string instead of empty
      // string. MainLayout.OpenDialog now accepts string targetId and parses with
      // Guid.TryParse → Guid.Empty fallback (Task 2), so this is a belt-and-suspenders
      // change that also guards if MainLayout is ever reverted.
      _dispatchOpenDialog(commandId, '00000000-0000-0000-0000-000000000000', '');
      closePalette();
    }
  });

  function _enterTargetPick(actionLabel) {
    var input = document.querySelector('.palette-input input');
    if (input) {
      input.value = '';
      input.placeholder = 'Search players for: ' + actionLabel;
      input.focus();
    }
    var list = document.querySelector('.palette-list');
    if (list) {
      // Replace via DOM mutation, NOT html-string assignment. Build a placeholder section first.
      while (list.firstChild) list.removeChild(list.firstChild);
      var hint = document.createElement('div');
      hint.className = 'palette-section';
      hint.textContent = 'Type to search players';
      list.appendChild(hint);
    }
    document.addEventListener('input', _onTargetSearchInput);
  }

  var _searchAbort = null;
  function _onTargetSearchInput(e) {
    if (!e.target || !e.target.matches) return;
    if (!e.target.matches('.palette-input input')) return;
    if (!_selectedAction) { document.removeEventListener('input', _onTargetSearchInput); return; }
    if (_searchAbort) { try { _searchAbort.abort(); } catch (err) {} }
    _searchAbort = (typeof AbortController !== 'undefined') ? new AbortController() : null;
    var q = e.target.value || '';
    if (q.length < 2) return;
    // Phase 03.1-10 gap closure (WARNING-02): read the resolved /admin (or custom) base
    // from window.GKAdminConfig (emitted by App.razor inline init). Falls back to the
    // default mount-path so the bundle still works if the host app omits the bootstrap.
    var apiBase = (window.GKAdminConfig && window.GKAdminConfig.apiBase) || '/admin/api';
    fetch(apiBase + '/players/search?query=' + encodeURIComponent(q),
      { credentials: 'same-origin', signal: _searchAbort ? _searchAbort.signal : undefined })
      .then(function (r) { return r.ok ? r.json() : { items: [] }; })
      .then(function (page) {
        // Phase 03.1-10 gap closure (BLOCKER-02): GET /<apiBase>/players/search returns
        // PaginatedResult<PlayerRow> shape { items, nextCursor, hasMore } — drill into
        // .items before passing to _renderTargetResults (which expects a flat array).
        var rows = (page && Array.isArray(page.items)) ? page.items : [];
        _renderTargetResults(rows);
      })
      .catch(function () { /* aborted or network error — silent */ });
  }

  function _renderTargetResults(rows) {
    var list = document.querySelector('.palette-list');
    if (!list) return;
    while (list.firstChild) list.removeChild(list.firstChild);
    if (!Array.isArray(rows) || rows.length === 0) {
      var empty = document.createElement('div');
      empty.className = 'palette-section';
      empty.textContent = 'No players match';
      list.appendChild(empty);
      return;
    }
    var sec = document.createElement('div');
    sec.className = 'palette-section';
    sec.textContent = 'Players';
    list.appendChild(sec);
    rows.forEach(function (p) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'palette-row';
      btn.setAttribute('role', 'option');
      btn.setAttribute('aria-selected', 'false');
      btn.setAttribute('data-target-id', p.id || '');
      btn.setAttribute('data-display-name', p.displayName || '');
      btn.setAttribute('data-label', p.displayName || '');
      // textContent — NEVER set HTML from a string — guards against XSS via display name.
      btn.textContent = p.displayName || '(unknown)';
      list.appendChild(btn);
    });
    _resetSelection();
  }

  function _dispatchOpenDialog(commandId, targetId, targetName) {
    // Two delivery paths: CustomEvent (host-app listeners) AND the registered DotNetObjectReference.
    try {
      window.dispatchEvent(new CustomEvent('gamekit.admin.openDialog', {
        detail: { commandId: commandId, targetId: targetId, displayName: targetName }
      }));
    } catch (e) { /* IE/old-browser noop */ }
    // Phase 03.1-10 gap closure (BLOCKER-03): never bridge an empty string into the C#
    // string→Guid path (even with TryParse fallback in MainLayout, '' is meaningful only
    // as Guid.Empty). Empty becomes the zero GUID literal; the C# side parses it.
    invokeDialog(commandId,
                 targetId || '00000000-0000-0000-0000-000000000000',
                 targetName || '');
  }

  // -------- Global keydown listener (Pitfall 3 — open even from textarea) -----
  window.addEventListener('keydown', function (e) {
    if ((e.metaKey || e.ctrlKey) && e.key && e.key.toLowerCase() === 'k') {
      e.preventDefault(); openPalette(); return;
    }
    if ((e.metaKey || e.ctrlKey) && e.key === '\\') {
      e.preventDefault(); toggleSidebar(); return;
    }
    // Palette navigation — only when the overlay is open
    if (document.documentElement.getAttribute('data-palette-open') === 'true') {
      if (e.key === 'ArrowDown')   { e.preventDefault(); _moveSelection(+1); return; }
      if (e.key === 'ArrowUp')     { e.preventDefault(); _moveSelection(-1); return; }
      if (e.key === 'Enter')       { e.preventDefault(); _activateSelection(); return; }
    }
    if (e.key === 'Escape') {
      closePalette(); closeTweaks();
    }
  });

  // Re-apply attrs in case the inline init missed (idempotent).
  applyAttrs(loadTweaks());

  // -------- Public API -------------------------------------------------------
  window.GKAdmin = {
    openPalette: openPalette,
    closePalette: closePalette,
    openTweaks: openTweaks,
    closeTweaks: closeTweaks,
    saveTweaks: saveTweaks,
    loadTweaks: loadTweaks,
    resetTweaks: resetTweaks,
    applyAttrs: applyAttrs,
    copyClipboard: copyClipboard,
    toggleAuditRow: toggleAuditRow,
    toggleSidebar: toggleSidebar,
    registerDialogBridge: registerDialogBridge,
    invokeDialog: invokeDialog
  };
})();
