// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
//
// app.js — Platformer3D flow controller (ES module). Owns the screen state machine and
// orchestrates auth + ranked quick-match + friend party + solo + results + leaderboard.
// Imports the engine + auth from game.js. SignalR is the window.signalR global (signalr.min.js).

import { guestSignIn, authFetch, getAccessToken, getPlayerId, runGame, disposeGame } from '/js/game.js';

const $ = (id) => document.getElementById(id);
const txt = (id, v) => { const el = $(id); if (el) el.textContent = v; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const escapeHtml = (s) => (s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const SCREENS = ['signin', 'menu', 'party', 'searching', 'game', 'results', 'leaderboard'];
function showScreen(name) {
  for (const s of SCREENS) $('screen-' + s)?.classList.toggle('active', s === name);
}

let _ladderId = null;
async function ladderId() {
  if (_ladderId) return _ladderId;
  const r = await fetch('/demo/ladder-id/platformer');
  _ladderId = (await r.json()).id;
  return _ladderId;
}

// ─── Sign-in ─────────────────────────────────────────────────────────────────
$('btn-guest').addEventListener('click', async () => {
  const btn = $('btn-guest'); btn.disabled = true; txt('signin-err', '');
  try { await guestSignIn(); await goMenu(); }
  catch (e) { txt('signin-err', e.message ?? 'sign-in failed'); btn.disabled = false; }
});

// ─── Menu ────────────────────────────────────────────────────────────────────
async function goMenu() {
  disposeGame();
  disconnectParty();
  showScreen('menu');
  await refreshRankChip();
}
async function fetchMyRank() {
  try { const r = await authFetch('/demo/my-rank'); return r.ok ? await r.json() : null; } catch { return null; }
}
async function refreshRankChip() {
  const rank = await fetchMyRank();
  if (rank?.hasRank) {
    txt('menu-rating', Math.round(rank.rating));
    txt('menu-wl', `${rank.wins}W · ${rank.losses}L${rank.draws ? ` · ${rank.draws}D` : ''}`);
    txt('menu-rank-note', rank.isInPlacement ? 'placement matches' : 'ranked');
  } else {
    txt('menu-rating', '—');
    txt('menu-wl', 'Unrated');
    txt('menu-rank-note', 'play a ranked match to earn a rating');
  }
}

$('btn-ranked').addEventListener('click', startRanked);
$('btn-party').addEventListener('click', () => { resetPartyUI(); showScreen('party'); });
$('btn-solo').addEventListener('click', startSolo);
$('btn-board').addEventListener('click', () => { showScreen('leaderboard'); loadLeaderboard(); });

// ─── Solo practice ───────────────────────────────────────────────────────────
async function startSolo() {
  showScreen('game');
  await runGame({ sessionId: null, mode: 'solo', onFinish: (r) => showResults({ mode: 'solo', timeMs: r.timeMs }) });
}

// ─── Shared matchmaking poll ─────────────────────────────────────────────────
let _searchCancel = false, _searchTicketId = null, _searchTimer = null;

function startSearchClock() {
  stopSearchClock();
  const t0 = Date.now();
  _searchTimer = setInterval(() => {
    const s = Math.floor((Date.now() - t0) / 1000);
    txt('search-time', `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`);
  }, 250);
}
function stopSearchClock() { if (_searchTimer) { clearInterval(_searchTimer); _searchTimer = null; } }

$('btn-cancel-search').addEventListener('click', async () => {
  _searchCancel = true; stopSearchClock();
  if (_searchTicketId) { try { await authFetch(`/api/mm/queue/${_searchTicketId}`, { method: 'DELETE' }); } catch {} }
  await goMenu();
});

// Poll a ticket through queued → proposed → matched; auto-accept proposals; start the match.
async function pollTicket(ticketId, ctx) {
  const deadline = Date.now() + 120000;
  let accepted = null;
  while (!_searchCancel && Date.now() < deadline) {
    let body;
    try {
      const r = await authFetch(`/api/mm/queue/${ticketId}/status`);
      if (!r.ok) { await sleep(800); continue; }
      body = await r.json();
    } catch { await sleep(800); continue; }
    if (_searchCancel) return;

    if (body.status === 'proposed' && body.proposalId && body.proposalId !== accepted) {
      accepted = body.proposalId;
      txt('search-status', 'Opponent found — accepting…');
      try {
        await authFetch(`/api/mm/proposal/${body.proposalId}/accept`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ticketId }),
        });
      } catch {}
      await sleep(300); continue;
    }
    if (body.status === 'matched' && body.sessionId) { stopSearchClock(); await startMatch(body.sessionId, ctx); return; }
    if (body.status === 'cancelled') { txt('search-status', 'Match cancelled.'); await sleep(1200); await goMenu(); return; }
    await sleep(700);
  }
  if (!_searchCancel) { txt('search-status', 'No opponent found — timed out.'); await sleep(1500); await goMenu(); }
}

// ─── Ranked quick-match ──────────────────────────────────────────────────────
async function startRanked() {
  _searchCancel = false; _searchTicketId = null;
  const ratingBefore = (await fetchMyRank())?.rating ?? null;
  txt('search-title', 'Searching for an opponent…');
  txt('search-sub', 'Ranked 1v1 — the fastest run wins.');
  txt('search-status', 'Tip: open a second browser profile to play the other side.');
  showScreen('searching');
  startSearchClock();
  try {
    const r = await authFetch('/demo/quick-match', { method: 'POST' });
    if (!r.ok) { txt('search-status', 'Could not join the ranked queue.'); return; }
    _searchTicketId = (await r.json()).ticketId;
    await pollTicket(_searchTicketId, { mode: 'ranked', ratingBefore });
  } catch (e) { txt('search-status', 'Queue error: ' + (e.message ?? e)); }
}

// ─── Friend party (unranked) ─────────────────────────────────────────────────
let _partyConn = null, _partyLobbyId = null;

function resetPartyUI() {
  $('party-pick').classList.remove('hidden');
  $('party-active').classList.add('hidden');
  $('party-code-wrap').classList.add('hidden');
  $('btn-ready').disabled = true;
  $('btn-create-party').disabled = false;
  $('btn-join-party').disabled = false;
  txt('party-status', ''); $('party-members').innerHTML = ''; $('invite-input').value = '';
  disconnectParty();
}
function disconnectParty() { if (_partyConn) { try { _partyConn.stop(); } catch {} _partyConn = null; } _partyLobbyId = null; }

$('btn-party-back').addEventListener('click', goMenu);
$('party-code').addEventListener('click', () => {
  navigator.clipboard?.writeText($('party-code').textContent).then(() => txt('party-status', 'Invite code copied!')).catch(() => {});
});

$('btn-create-party').addEventListener('click', async () => {
  $('btn-create-party').disabled = true; $('btn-join-party').disabled = true;
  try {
    await authFetch('/demo/leave-party', { method: 'POST' }).catch(() => {}); // clear any lingering party
    const lid = await ladderId();
    const r = await authFetch('/api/lobbies', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ maxMembers: 2, ladderId: lid }),
    });
    if (!r.ok) throw new Error('create failed (' + r.status + ')');
    const b = await r.json(); _partyLobbyId = b.lobbyId;
    $('party-pick').classList.add('hidden');
    $('party-active').classList.remove('hidden');
    $('party-code-wrap').classList.remove('hidden');
    txt('party-code', b.lobbyId);
    $('party-members').innerHTML = '<span class="wait">●</span> You · waiting for a friend to join…';
    txt('party-status', 'Share the invite code. Ready unlocks once your friend joins.');
    await connectParty(b.lobbyId, b.state);
  } catch (e) { txt('party-status', 'Error: ' + (e.message ?? e)); resetPartyUI(); }
});

$('btn-join-party').addEventListener('click', async () => {
  const code = $('invite-input').value.trim();
  if (!code) { txt('party-status', 'Enter an invite code first.'); return; }
  $('btn-create-party').disabled = true; $('btn-join-party').disabled = true;
  try {
    await authFetch('/demo/leave-party', { method: 'POST' }).catch(() => {}); // clear any lingering party
    const r = await authFetch(`/api/lobbies/${code}/join`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lobbyId: code }),
    });
    if (!r.ok) throw new Error('join failed (' + r.status + ')');
    const b = await r.json(); _partyLobbyId = code;
    $('party-pick').classList.add('hidden');
    $('party-active').classList.remove('hidden');
    txt('party-status', 'Joined the party.');
    await connectParty(code, b.state);
  } catch (e) { txt('party-status', 'Error: ' + (e.message ?? e)); resetPartyUI(); }
});

async function connectParty(lobbyId, knownState) {
  const signalR = window.signalR;
  if (!signalR) { txt('party-status', 'SignalR not loaded.'); return; }
  _partyConn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/lobby', { accessTokenFactory: () => getAccessToken() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  _partyConn.on('ReceiveStateUpdateAsync', (upd) => onPartyState(upd.State ?? upd.state));
  await _partyConn.start();
  await _partyConn.invoke('JoinLobbyAsync', lobbyId);
  // A joiner may connect AFTER the auto Open→ReadyChecking broadcast — reconcile from the
  // state returned by the join/create REST call.
  if (knownState === 'ReadyChecking') onPartyState(1);
}

function onPartyState(state) {
  if (state === 1) {
    $('btn-ready').disabled = false;
    $('party-members').innerHTML = '<span class="rdy">●</span> You &nbsp;&nbsp; <span class="rdy">●</span> Friend — both here!';
    txt('party-status', 'Both players here — click Ready!');
  } else if (state === 3) {
    $('btn-ready').disabled = true;
    txt('party-status', 'Both ready! Starting your match…');
    startPartyMatch();
  }
}

$('btn-ready').addEventListener('click', async () => {
  if (!_partyConn || !_partyLobbyId) return;
  $('btn-ready').disabled = true;
  txt('party-status', 'Ready! Waiting for your friend…');
  try { await _partyConn.invoke('MarkReadyAsync', _partyLobbyId); }
  catch (e) { txt('party-status', 'Ready failed: ' + (e.message ?? e)); $('btn-ready').disabled = false; }
});

async function startPartyMatch() {
  _searchCancel = false; _searchTicketId = null;
  txt('search-title', 'Starting your match…');
  txt('search-sub', 'Friendly 1v1 — unranked, just for fun.');
  txt('search-status', '');
  showScreen('searching');
  startSearchClock();
  const ticketId = await discoverMyTicket();
  disconnectParty();
  if (!ticketId) { txt('search-status', 'Could not find the match ticket.'); await sleep(1500); await goMenu(); return; }
  _searchTicketId = ticketId;
  await pollTicket(ticketId, { mode: 'casual', ratingBefore: null });
}

async function discoverMyTicket() {
  for (let i = 0; i < 20 && !_searchCancel; i++) {
    try { const r = await authFetch('/demo/my-ticket'); if (r.ok) { const b = await r.json(); if (b.ticketId) return b.ticketId; } } catch {}
    await sleep(500);
  }
  return null;
}

// ─── Match start + finish ────────────────────────────────────────────────────
async function startMatch(sessionId, ctx) {
  showScreen('game');
  await runGame({ sessionId, mode: ctx.mode, onFinish: (r) => onMatchFinish(r, { ...ctx, sessionId }) });
}

async function onMatchFinish(r, ctx) {
  if (r.rejected) { showResults({ mode: ctx.mode, rejected: true, reason: r.reason }); return; }

  showResults({ mode: ctx.mode, pending: true, myTimeMs: r.completionMs });
  const final = await waitForSessionResult(ctx.sessionId);

  const me = getPlayerId();
  let myResult = null, myTime = r.completionMs, oppTime = null;
  if (final?.participants) {
    for (const p of final.participants) {
      if (p.playerId === me) { myResult = p.result; myTime = p.timeMs ?? myTime; }
      else { oppTime = p.timeMs; }
    }
  }

  let delta = null, ratingAfter = null, ratingBefore = ctx.ratingBefore;
  if (ctx.mode === 'ranked') {
    if (ratingBefore == null) ratingBefore = 1000; // first ranked match: ladder DefaultRating baseline
    ratingAfter = await waitForRatingChange(ratingBefore);
    if (ratingAfter != null) delta = Math.round(ratingAfter) - Math.round(ratingBefore);
  }

  showResults({
    mode: ctx.mode, myResult, myTimeMs: myTime, oppTimeMs: oppTime,
    ratingBefore, ratingAfter, delta, completed: !!final?.completed,
  });
}

async function waitForSessionResult(sessionId) {
  const deadline = Date.now() + 45000;
  let last = null;
  while (Date.now() < deadline) {
    try { const r = await authFetch(`/demo/session-result/${sessionId}`); if (r.ok) { last = await r.json(); if (last.completed) return last; } } catch {}
    await sleep(1000);
  }
  return last;
}

async function waitForRatingChange(before) {
  const deadline = Date.now() + 14000;
  while (Date.now() < deadline) {
    const rank = await fetchMyRank();
    if (rank?.hasRank && Math.round(rank.rating) !== Math.round(before)) return rank.rating;
    await sleep(1000);
  }
  const rank = await fetchMyRank();
  return rank?.hasRank ? rank.rating : null;
}

// ─── Results screen ──────────────────────────────────────────────────────────
function timeBox(lbl, ms, you) {
  const val = (ms == null || ms >= 2147483647) ? 'DNF' : `${(ms / 1000).toFixed(2)}s`;
  return `<div class="time-box${you ? ' you' : ''}"><div class="lbl">${lbl}</div><div class="val">${val}</div></div>`;
}

function showResults(d) {
  showScreen('results');
  const banner = $('result-banner');
  txt('result-rating', ''); $('result-rating').innerHTML = ''; txt('result-note', '');

  if (d.rejected) {
    banner.className = 'result-banner lose'; banner.textContent = 'Run rejected';
    $('result-times').innerHTML = ''; txt('result-note', `The server rejected the run: ${d.reason}`); return;
  }
  if (d.mode === 'solo') {
    banner.className = 'result-banner solo'; banner.textContent = 'Practice complete';
    $('result-times').innerHTML = timeBox('Your time', d.timeMs, true);
    txt('result-note', 'Solo practice — no opponent, no rating.'); return;
  }
  if (d.pending) {
    banner.className = 'result-banner solo'; banner.textContent = 'You finished! 🏁';
    $('result-times').innerHTML = timeBox('Your time', d.myTimeMs, true);
    txt('result-note', 'Waiting for your opponent to finish…'); return;
  }

  const res = (d.myResult || '').toLowerCase();
  banner.className = 'result-banner ' + (res === 'win' ? 'win' : res === 'loss' ? 'lose' : res === 'draw' ? 'draw' : 'solo');
  banner.textContent = res === 'win' ? 'Victory! 🏆' : res === 'loss' ? 'Defeat' : res === 'draw' ? 'Draw' : 'Match over';
  $('result-times').innerHTML = timeBox('Your time', d.myTimeMs, true) + timeBox('Opponent', d.oppTimeMs, false);

  if (d.mode === 'ranked') {
    if (d.delta != null && d.ratingAfter != null) {
      const up = d.delta >= 0;
      $('result-rating').innerHTML =
        `<span class="${up ? 'up' : 'down'}">${Math.round(d.ratingBefore)} → <span class="num">${Math.round(d.ratingAfter)}</span> &nbsp;(${up ? '+' : ''}${d.delta})</span>`;
    } else {
      txt('result-note', d.completed ? 'Rating updating…' : 'Match recorded.');
    }
  } else {
    txt('result-note', 'Friendly match — unranked, no rating change.');
  }
}

$('btn-results-again').addEventListener('click', goMenu);
$('btn-results-board').addEventListener('click', () => { showScreen('leaderboard'); loadLeaderboard(); });

// ─── Leaderboard ─────────────────────────────────────────────────────────────
$('btn-lb-refresh').addEventListener('click', loadLeaderboard);
$('btn-lb-back').addEventListener('click', goMenu);

async function loadLeaderboard() {
  const body = $('lb-body'); body.innerHTML = '<p class="muted">Loading…</p>';
  try {
    const r = await fetch('/demo/leaderboard');
    if (!r.ok) { body.innerHTML = '<p class="muted">Leaderboard unavailable.</p>'; return; }
    const rows = await r.json();
    if (!rows.length) { body.innerHTML = '<p class="muted">No ranked players yet — play a ranked match to get on the board!</p>'; return; }
    const me = getPlayerId();
    let html = '<table class="lb"><thead><tr><th>#</th><th>Player</th><th class="r">Rating</th><th class="r">W</th><th class="r">L</th></tr></thead><tbody>';
    for (const row of rows.slice(0, 15)) {
      const isMe = row.playerId === me;
      const name = row.displayName ?? (row.playerId ? row.playerId.slice(0, 8) + '…' : '?');
      html += `<tr class="${isMe ? 'me' : ''}"><td>${row.rank}</td><td>${escapeHtml(name)}${isMe ? ' (you)' : ''}</td>`
            + `<td class="r">${Math.round(row.rating)}</td><td class="r">${row.wins}</td><td class="r">${row.losses}</td></tr>`;
    }
    body.innerHTML = html + '</tbody></table>';
  } catch { body.innerHTML = '<p class="muted">Leaderboard error.</p>'; }
}

// ─── Boot ────────────────────────────────────────────────────────────────────
showScreen('signin');
