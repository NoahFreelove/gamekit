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
    if (_paletteOpener && _paletteOpener.focus) _paletteOpener.focus();
    _paletteOpener = null;
  }
  function openTweaks() {
    _tweaksOpener = document.activeElement;
    document.documentElement.setAttribute('data-tweaks-open', 'true');
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
    if (e.target === document.querySelector('.palette-scrim')) closePalette();
    if (e.target === document.querySelector('.tweaks-scrim'))  closeTweaks();
  });

  // -------- Global keydown listener (Pitfall 3 — open even from textarea) -----
  window.addEventListener('keydown', function (e) {
    if ((e.metaKey || e.ctrlKey) && e.key && e.key.toLowerCase() === 'k') {
      e.preventDefault(); openPalette(); return;
    }
    if ((e.metaKey || e.ctrlKey) && e.key === '\\') {
      e.preventDefault(); toggleSidebar(); return;
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
