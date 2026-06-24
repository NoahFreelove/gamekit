// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// lobby.js — Platformer3D browser lobby/party + matchmaking wiring (ES module)
//
// Accesses the `signalR` global exposed by the IIFE bundle in signalr.min.js,
// which must be loaded via <script> BEFORE this module runs.
//
// LobbyState integer values (from src/GameKit.Lobby/Entities/LobbyState.cs):
//   0 = Open, 1 = ReadyChecking, 2 = Closed, 3 = InGame

// ─── Module state ────────────────────────────────────────────────────────────
let _conn        = null;    // SignalR HubConnection
let _lobbyId     = null;    // current lobby guid
let _ticketId    = null;    // matchmaking ticket guid
let _getToken    = null;    // () => string — access token factory
let _inGameMode  = false;   // true when controls are wired to in-game -mp panel

// ─── UI element refs (resolved once on initLobbyControls) ────────────────────
let _btnCreate   = null;
let _btnJoin     = null;
let _btnReady    = null;
let _codeInput   = null;
let _codeDisplay = null;
let _statusText  = null;
let _lbArea      = null;

// ─── Helpers ─────────────────────────────────────────────────────────────────

function setLobbyStatus(msg) {
  console.log('[lobby]', msg);
  if (_statusText) _statusText.textContent = msg;
}

function showLobbyCode(code) {
  if (_codeDisplay) {
    _codeDisplay.textContent = `Your code: ${code}`;
    _codeDisplay.style.display = 'inline';
  }
}

function enableReadyButton(on) {
  if (_btnReady) _btnReady.disabled = !on;
}

async function authFetch(url, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  if (_getToken) headers['Authorization'] = 'Bearer ' + _getToken();
  const resp = await fetch(url, { ...opts, headers });
  return resp;
}

// ─── Ticket discovery ────────────────────────────────────────────────────────

async function pollForTicketId(maxAttempts = 20) {
  // After the InGame broadcast, the server may need a few hundred ms to commit the ticket.
  // Retry with 500ms delay up to maxAttempts times (~10s total).
  for (let i = 0; i < maxAttempts; i++) {
    const resp = await authFetch('/demo/my-ticket');
    if (resp.ok) {
      const body = await resp.json();
      return body.ticketId;
    }
    setLobbyStatus(`Waiting for ticket… (attempt ${i + 1}/${maxAttempts})`);
    await new Promise(r => setTimeout(r, 500));
  }
  throw new Error('no_active_ticket: matchmaking ticket not found within 10s');
}

// ─── Match poll loop ─────────────────────────────────────────────────────────

async function startMatchPoll() {
  setLobbyStatus('Discovering matchmaking ticket…');
  _ticketId = await pollForTicketId();
  setLobbyStatus(`Ticket found (${_ticketId.slice(0, 8)}…). Waiting for match…`);

  const MAX_POLL_MS = 120_000;
  const deadline = Date.now() + MAX_POLL_MS;

  while (Date.now() < deadline) {
    let resp;
    try {
      resp = await authFetch(`/api/mm/queue/${_ticketId}/status`);
    } catch (e) {
      setLobbyStatus('Poll error — retrying…');
      await new Promise(r => setTimeout(r, 1000));
      continue;
    }

    if (!resp.ok) {
      setLobbyStatus(`Poll returned ${resp.status} — retrying…`);
      await new Promise(r => setTimeout(r, 1000));
      continue;
    }

    const body = await resp.json();

    if (body.status === 'proposed' && body.proposalId) {
      setLobbyStatus('Match proposed! Accepting…');
      try {
        await authFetch(`/api/mm/proposal/${body.proposalId}/accept`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ticketId: _ticketId }),
        });
        setLobbyStatus('Proposal accepted. Waiting for opponent acceptance…');
      } catch (e) {
        setLobbyStatus('Accept error — continuing poll…');
      }
      // Resume poll — next iteration will return matched or queued
      await new Promise(r => setTimeout(r, 300));
      continue;
    }

    if (body.status === 'matched' && body.sessionId) {
      const sessionId = body.sessionId;
      setLobbyStatus(`Matched! Session ${sessionId.slice(0, 8)}… — starting game…`);
      disconnectHub();
      // Start the 3D competitive game (replaces the current solo run)
      if (typeof window.startGame === 'function') {
        window.startGame(sessionId);
      } else {
        console.error('[lobby] window.startGame not defined — cannot start game');
      }
      // After match: fetch and display leaderboard
      fetchAndShowLeaderboard().catch(e => console.warn('[lobby] leaderboard fetch failed', e));
      return;
    }

    if (body.status === 'cancelled') {
      setLobbyStatus('Matchmaking cancelled. Refresh to try again.');
      return;
    }

    // status === 'queued' — long-poll returned; loop immediately
    setLobbyStatus('In matchmaking queue… seeking opponent…');
  }

  setLobbyStatus('Matchmaking timed out (2 min). Refresh to retry.');
}

// ─── Leaderboard ─────────────────────────────────────────────────────────────

async function fetchAndShowLeaderboard() {
  const resp = await fetch('/demo/leaderboard');
  if (!resp.ok) return;
  const rows = await resp.json();
  if (!_lbArea) return;

  let html = '<h4 style="color:#90caf9;margin:0.5rem 0 0.25rem">Platformer Leaderboard</h4>';
  html += '<table style="border-collapse:collapse;font-size:0.8rem;width:100%">';
  html += '<thead><tr><th>#</th><th>Name</th><th>Rating</th><th>W</th><th>L</th></tr></thead><tbody>';
  for (const row of rows.slice(0, 10)) {
    const name = row.displayName ?? row.playerId?.slice(0, 8) + '…' ?? '?';
    html += `<tr>
      <td style="padding:2px 4px">${row.rank}</td>
      <td style="padding:2px 4px">${escapeHtml(name)}</td>
      <td style="padding:2px 4px">${Math.round(row.rating)}</td>
      <td style="padding:2px 4px">${row.wins}</td>
      <td style="padding:2px 4px">${row.losses}</td>
    </tr>`;
  }
  html += '</tbody></table>';
  _lbArea.innerHTML = html;
  _lbArea.style.display = 'block';
}

function escapeHtml(str) {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// ─── SignalR hub connection ───────────────────────────────────────────────────

async function connectHub() {
  if (_conn) { disconnectHub(); }

  const signalR = window.signalR;
  if (!signalR) {
    throw new Error('signalR global not found — ensure signalr.min.js is loaded before lobby.js');
  }

  _conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/lobby', {
      accessTokenFactory: () => _getToken ? _getToken() : '',
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  _conn.on('ReceiveStateUpdateAsync', handleStateUpdate);

  await _conn.start();   // SignalR JS client lifecycle method is start() (not the C# startAsync())
  await _conn.invoke('JoinLobbyAsync', _lobbyId);
  setLobbyStatus('Connected to lobby. Click Ready when you are ready to race!');
  enableReadyButton(true);
}

function disconnectHub() {
  if (_conn) {
    _conn.stop().catch(() => {});
    _conn = null;
  }
}

// ─── Hub event handler ────────────────────────────────────────────────────────

async function handleStateUpdate(upd) {
  // upd.State is an integer (default STJ JSON protocol — numeric enums)
  // 0=Open, 1=ReadyChecking, 2=Closed, 3=InGame  (LobbyState enum)
  if (upd.State === 1) {
    setLobbyStatus('All players joined! Ready check started — click Ready!');
    enableReadyButton(true);
  } else if (upd.State === 3) {
    setLobbyStatus('All ready! Entering matchmaking — please wait…');
    enableReadyButton(false);
    await startMatchPoll();
  }
}

// ─── Create lobby ─────────────────────────────────────────────────────────────

async function createLobby(ladderId) {
  setLobbyStatus('Creating lobby…');
  const resp = await authFetch('/api/lobbies', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ maxMembers: 2, ladderId }),
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`Create lobby failed (${resp.status}): ${text}`);
  }
  const body = await resp.json();
  _lobbyId = body.lobbyId;
  showLobbyCode(_lobbyId);
  setLobbyStatus(`Lobby created. Share your code: ${_lobbyId}`);
  await connectHub();
}

// ─── Join lobby ───────────────────────────────────────────────────────────────

async function joinLobby(inviteCode) {
  const code = inviteCode.trim();
  if (!code) {
    setLobbyStatus('Enter a lobby code first.');
    return;
  }
  _lobbyId = code;
  setLobbyStatus(`Joining lobby ${code.slice(0, 8)}…`);
  const resp = await authFetch(`/api/lobbies/${_lobbyId}/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ lobbyId: _lobbyId }),
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`Join lobby failed (${resp.status}): ${text}`);
  }
  // On 200: joined; server auto-transitions Open→ReadyChecking when MaxMembers reached
  setLobbyStatus(`Joined lobby. Connecting to hub…`);
  await connectHub();
}

// ─── Public init — called by game.js after sign-in ───────────────────────────
//
// inGameMode=true: wire to the -mp suffixed elements inside #game-section
// inGameMode=false (default): wire to the original auth-screen elements

export function initLobbyControls(getToken, inGameMode = false) {
  _getToken    = getToken;
  _inGameMode  = inGameMode;

  if (inGameMode) {
    // In-game multiplayer panel (inside #game-section, toggled by Multiplayer button)
    _btnCreate   = document.getElementById('btn-create-lobby-mp');
    _btnJoin     = document.getElementById('btn-join-lobby-mp');
    _btnReady    = document.getElementById('btn-ready-mp');
    _codeInput   = document.getElementById('invite-code-input-mp');
    _codeDisplay = document.getElementById('lobby-code-display-mp');
    _statusText  = document.getElementById('mp-status');
    _lbArea      = document.getElementById('mp-lb-area');
  } else {
    // Original auth-screen panel
    _btnCreate   = document.getElementById('btn-create-lobby');
    _btnJoin     = document.getElementById('btn-join-lobby');
    _btnReady    = document.getElementById('btn-ready');
    _codeInput   = document.getElementById('invite-code-input');
    _codeDisplay = document.getElementById('lobby-code-display');
    _statusText  = document.getElementById('lobby-status-text');
    _lbArea      = document.getElementById('leaderboard-area');
  }

  // Enable create/join buttons (they were disabled until sign-in)
  if (_btnCreate) _btnCreate.disabled = false;
  if (_btnJoin)   _btnJoin.disabled   = false;
  setLobbyStatus('Signed in. Create a party or enter an invite code to join one.');

  // Wire Create
  if (_btnCreate) {
    _btnCreate.addEventListener('click', async () => {
      _btnCreate.disabled = true;
      try {
        // Resolve ladder ID first
        const ladderResp = await fetch('/demo/ladder-id/platformer');
        if (!ladderResp.ok) throw new Error('Could not resolve platformer ladder ID');
        const { id: ladderId } = await ladderResp.json();
        await createLobby(ladderId);
      } catch (err) {
        setLobbyStatus(`Error: ${err.message}`);
        _btnCreate.disabled = false;
      }
    });
  }

  // Wire Join
  if (_btnJoin) {
    _btnJoin.addEventListener('click', async () => {
      _btnJoin.disabled = true;
      try {
        await joinLobby(_codeInput?.value ?? '');
      } catch (err) {
        setLobbyStatus(`Error: ${err.message}`);
        _btnJoin.disabled = false;
      }
    });
  }

  // Wire Ready
  if (_btnReady) {
    _btnReady.addEventListener('click', async () => {
      if (!_conn || !_lobbyId) {
        setLobbyStatus('Not connected to a lobby yet.');
        return;
      }
      _btnReady.disabled = true;
      setLobbyStatus('Sending Ready…');
      try {
        await _conn.invoke('MarkReadyAsync', _lobbyId);
        setLobbyStatus('Ready sent! Waiting for all players…');
      } catch (err) {
        setLobbyStatus(`Ready failed: ${err.message}`);
        _btnReady.disabled = false;
      }
    });
  }
}

// Expose as window property for non-module usage (game.js bootstrap calls this)
window.initLobbyControls = initLobbyControls;

// Also expose fetchAndShowLeaderboard so the game can trigger it post-match
window.showLeaderboard = fetchAndShowLeaderboard;
