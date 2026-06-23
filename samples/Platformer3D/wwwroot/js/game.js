// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// game.js — Platformer3D three.js game loop (ES module, no bundler)
// Imports three.js from the vendored local module (no CDN).

import * as THREE from '/js/three.module.js';
import { PointerLockControls } from '/js/addons/PointerLockControls.js';

// -------------------------------------------------------------------------
// Auth state (in module scope — not persisted to localStorage to reduce XSS
// window for the access token; refresh is stored per RESEARCH XSS note)
// -------------------------------------------------------------------------
let _accessToken = null;
let _refreshToken = null;
const KEY_REFRESH = 'gk.refresh_token';
const KEY_DEVICE  = 'gk.device_id';

function getOrCreateDeviceId() {
  let id = localStorage.getItem(KEY_DEVICE);
  if (!id) { id = crypto.randomUUID(); localStorage.setItem(KEY_DEVICE, id); }
  return id;
}

function decodeJwtPayload(jwt) {
  try {
    const parts = jwt.split('.');
    if (parts.length !== 3) return null;
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(b64));
  } catch { return null; }
}

// POST /auth/login/guest — returns { accessToken, refreshToken }.
// Access token held in module memory only (reduces XSS persistence risk).
export async function guestSignIn() {
  const resp = await fetch('/auth/login/guest', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-GameKit-Device': getOrCreateDeviceId() },
    body: '{}',
  });
  if (!resp.ok) throw new Error(`guest login failed (${resp.status})`);
  const body = await resp.json();
  _accessToken  = body.accessToken  ?? body.access_token;
  _refreshToken = body.refreshToken ?? body.refresh_token;
  if (_refreshToken) localStorage.setItem(KEY_REFRESH, _refreshToken);
  return decodeJwtPayload(_accessToken);
}

// Auth-aware fetch — carries Bearer JWT + device header.
export async function authFetch(url, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  headers['X-GameKit-Device'] = getOrCreateDeviceId();
  if (_accessToken) headers['Authorization'] = 'Bearer ' + _accessToken;
  const resp = await fetch(url, { ...opts, headers });
  // Refresh on 401 (one retry)
  if (resp.status === 401) {
    const refresh = _refreshToken ?? localStorage.getItem(KEY_REFRESH);
    if (refresh) {
      const rr = await fetch('/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-GameKit-Device': getOrCreateDeviceId() },
        body: JSON.stringify({ refreshToken: refresh }),
      });
      if (rr.ok) {
        const rb = await rr.json();
        _accessToken  = rb.accessToken  ?? rb.access_token;
        _refreshToken = rb.refreshToken ?? rb.refresh_token ?? refresh;
        if (_refreshToken) localStorage.setItem(KEY_REFRESH, _refreshToken);
        headers['Authorization'] = 'Bearer ' + _accessToken;
        return fetch(url, { ...opts, headers });
      }
    }
  }
  return resp;
}

// -------------------------------------------------------------------------
// Run timing (D-02: integer-ms precision)
// -------------------------------------------------------------------------
let _runStartMs    = 0;
let _checkpoints   = [];   // [{index, timestampMs}]
let _runFinished   = false;

function nowMs() { return Math.trunc(performance.now() + performance.timeOrigin); }

// -------------------------------------------------------------------------
// WebSocket run-summary submission
// -------------------------------------------------------------------------
async function submitRunSummary(matchId, finishMs) {
  if (!matchId) {
    updateStatus('Run complete (no active match — no run-summary sent).');
    return;
  }

  const wsProto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const wsUrl   = `${wsProto}//${location.host}/ws/game/${matchId}`;

  let ws;
  try {
    ws = new WebSocket(wsUrl, [], {
      headers: { Authorization: 'Bearer ' + _accessToken },
    });
  } catch {
    // Browsers don't support custom WS headers — use subprotocol trick or fall back.
    ws = new WebSocket(wsUrl);
  }

  ws.addEventListener('open', () => {
    // 1. run_start
    ws.send(JSON.stringify({ type: 'run_start', matchId, startMs: _runStartMs }));
    // 2. ordered checkpoints
    for (const cp of _checkpoints) {
      ws.send(JSON.stringify({ type: 'checkpoint', index: cp.index, timestampMs: cp.timestampMs }));
    }
    // 3. run_finish (D-02 integer-ms)
    ws.send(JSON.stringify({ type: 'run_finish', finishMs }));
  });

  ws.addEventListener('message', (ev) => {
    let msg;
    try { msg = JSON.parse(ev.data); } catch { return; }
    if (msg.type === 'ping') {
      ws.send(JSON.stringify({ type: 'pong' }));
    } else if (msg.type === 'validated') {
      const secs = ((msg.completionMs ?? 0) / 1000).toFixed(2);
      updateStatus(`Run validated! Completion time: ${secs}s (session ${msg.sessionId ?? '?'})`);
      ws.close(1000, 'run complete');
    } else if (msg.type === 'rejected') {
      updateStatus(`Run rejected by server: ${msg.reason ?? 'unknown reason'}`);
      ws.close(1000, 'run rejected');
    }
  });

  ws.addEventListener('error', (ev) => {
    console.warn('[game] WebSocket error', ev);
    updateStatus('Could not submit run-summary (WebSocket error).');
  });

  ws.addEventListener('close', (ev) => {
    if (ev.code !== 1000) {
      console.warn('[game] WS closed unexpectedly', ev.code, ev.reason);
    }
  });
}

// -------------------------------------------------------------------------
// Level geometry helpers
// -------------------------------------------------------------------------
function buildPlatformMesh(p) {
  const geo = new THREE.BoxGeometry(p.w, p.h, p.d);
  const mat = new THREE.MeshLambertMaterial({ color: p.color });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.set(p.x, p.y - p.h / 2, p.z);
  mesh.receiveShadow = true;
  mesh.castShadow = true;
  return mesh;
}

function buildCheckpointMarker(cp) {
  const geo = new THREE.SphereGeometry(0.5, 16, 8);
  const mat = new THREE.MeshLambertMaterial({ color: cp.color, transparent: true, opacity: 0.8 });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.set(cp.x, cp.y, cp.z);
  return mesh;
}

function buildFinishMarker(f) {
  const geo = new THREE.CylinderGeometry(f.radius * 0.3, f.radius * 0.3, 2, 16);
  const mat = new THREE.MeshLambertMaterial({ color: f.color, transparent: true, opacity: 0.9 });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.set(f.x, f.y, f.z);
  return mesh;
}

// AABB overlap check (player is ~0.5 radius sphere; platforms are boxes)
function playerOnPlatform(playerPos, platform) {
  const hw = platform.w / 2;
  const hd = platform.d / 2;
  const topY = platform.y;   // platform.y is top surface (from level.json: y is top)
  // Actually from JSON: y is the centre minus half-height
  const surfaceY = platform.y;   // platform mesh placed at y - h/2
  if (
    playerPos.x >= platform.x - hw - 0.5 &&
    playerPos.x <= platform.x + hw + 0.5 &&
    playerPos.z >= platform.z - hd - 0.5 &&
    playerPos.z <= platform.z + hd + 0.5
  ) {
    const footY = playerPos.y - 0.9;  // player feet offset
    if (footY <= surfaceY + 0.15 && footY >= surfaceY - 0.5) {
      return surfaceY;
    }
  }
  return null;
}

function distTo(a, b) {
  const dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

// -------------------------------------------------------------------------
// Status overlay
// -------------------------------------------------------------------------
function updateStatus(msg) {
  const el = document.getElementById('game-status');
  if (el) el.textContent = msg;
}

// -------------------------------------------------------------------------
// Main init
// -------------------------------------------------------------------------
export async function initGame(matchId) {
  const canvas  = document.getElementById('game-canvas');
  const overlay = document.getElementById('game-overlay');

  // Load level
  const lvlResp = await fetch('/assets/level.json');
  const level   = await lvlResp.json();

  // Scene
  const scene    = new THREE.Scene();
  scene.background = new THREE.Color(level.sky ?? '#1a237e');
  scene.fog = new THREE.Fog(level.fog?.color ?? '#1a237e', level.fog?.near ?? 30, level.fog?.far ?? 120);

  // Camera
  const camera = new THREE.PerspectiveCamera(75, canvas.clientWidth / canvas.clientHeight, 0.1, 500);

  // Renderer
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(canvas.clientWidth, canvas.clientHeight);
  renderer.shadowMap.enabled = true;

  window.addEventListener('resize', () => {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    renderer.setSize(w, h);
  });

  // Lights
  const ambient = new THREE.AmbientLight('#ffffff', 0.5);
  scene.add(ambient);
  const sun = new THREE.DirectionalLight('#ffffff', 1.2);
  sun.position.set(20, 40, 10);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  scene.add(sun);

  // Build platforms
  const platforms = level.platforms ?? [];
  for (const p of platforms) {
    scene.add(buildPlatformMesh(p));
  }

  // Build checkpoint markers
  const cpMarkers = [];
  const cpState   = (level.checkpoints ?? []).map(() => false);
  let   nextCpIdx = 0;
  for (const cp of level.checkpoints ?? []) {
    const m = buildCheckpointMarker(cp);
    scene.add(m);
    cpMarkers.push(m);
  }

  // Build finish marker
  const finishDef = level.finish;
  let   finishMarker = null;
  if (finishDef) {
    finishMarker = buildFinishMarker(finishDef);
    scene.add(finishMarker);
  }

  // PointerLock controls
  const controls = new PointerLockControls(camera, document.body);

  // Player state
  const spawn = level.spawn ?? { x: 0, y: 1.0, z: 0 };
  const playerPos = new THREE.Vector3(spawn.x, spawn.y, spawn.z);
  const velocity  = new THREE.Vector3(0, 0, 0);
  let   onGround  = false;

  const GRAVITY      = level.gravity       ?? -18;
  const JUMP_VEL     = level.jumpVelocity  ?? 8;
  const MOVE_SPEED   = level.moveSpeed     ?? 6;
  const RESPAWN_Y    = -20;

  const keys = { w: false, a: false, s: false, d: false, space: false };

  document.addEventListener('keydown', (e) => {
    const k = e.code;
    if (k === 'KeyW' || k === 'ArrowUp')    keys.w = true;
    if (k === 'KeyS' || k === 'ArrowDown')  keys.s = true;
    if (k === 'KeyA' || k === 'ArrowLeft')  keys.a = true;
    if (k === 'KeyD' || k === 'ArrowRight') keys.d = true;
    if (k === 'Space') { e.preventDefault(); keys.space = true; }
  });
  document.addEventListener('keyup', (e) => {
    const k = e.code;
    if (k === 'KeyW' || k === 'ArrowUp')    keys.w = false;
    if (k === 'KeyS' || k === 'ArrowDown')  keys.s = false;
    if (k === 'KeyA' || k === 'ArrowLeft')  keys.a = false;
    if (k === 'KeyD' || k === 'ArrowRight') keys.d = false;
    if (k === 'Space') keys.space = false;
  });

  // Lock on canvas click
  canvas.addEventListener('click', () => {
    if (!_runFinished) controls.lock();
  });

  controls.addEventListener('lock',   () => { if (overlay) overlay.classList.add('hidden'); });
  controls.addEventListener('unlock', () => { if (overlay) overlay.classList.remove('hidden'); });

  // Position camera
  camera.position.copy(playerPos);

  // Run state
  _runStartMs  = 0;
  _checkpoints = [];
  _runFinished = false;
  let runStarted = false;

  // Game loop
  const clock = new THREE.Clock();

  function animate() {
    requestAnimationFrame(animate);
    const dt = Math.min(clock.getDelta(), 0.1);  // cap dt to 100ms

    if (controls.isLocked && !_runFinished) {

      // --- start run on first movement ---
      if (!runStarted && (keys.w || keys.a || keys.s || keys.d || keys.space)) {
        runStarted   = true;
        _runStartMs  = nowMs();
        updateStatus('Run started — reach all checkpoints then the finish!');
      }

      // --- movement ---
      const forward = new THREE.Vector3();
      camera.getWorldDirection(forward);
      forward.y = 0;
      forward.normalize();
      const right = new THREE.Vector3();
      right.crossVectors(forward, new THREE.Vector3(0, 1, 0)).normalize();

      const move = new THREE.Vector3();
      if (keys.w) move.addScaledVector(forward,  MOVE_SPEED);
      if (keys.s) move.addScaledVector(forward, -MOVE_SPEED);
      if (keys.d) move.addScaledVector(right,    MOVE_SPEED);
      if (keys.a) move.addScaledVector(right,   -MOVE_SPEED);

      playerPos.x += move.x * dt;
      playerPos.z += move.z * dt;

      // --- gravity ---
      velocity.y += GRAVITY * dt;
      playerPos.y += velocity.y * dt;

      // --- collision: find highest platform player is on ---
      let highestSurface = null;
      for (const p of platforms) {
        const sy = playerOnPlatform(playerPos, p);
        if (sy !== null && (highestSurface === null || sy > highestSurface)) {
          highestSurface = sy;
        }
      }

      if (highestSurface !== null && playerPos.y - 0.9 <= highestSurface + 0.15) {
        playerPos.y = highestSurface + 0.9;
        if (velocity.y < 0) velocity.y = 0;
        onGround = true;
      } else {
        onGround = false;
      }

      // --- jump ---
      if (keys.space && onGround) {
        velocity.y = JUMP_VEL;
        onGround = false;
      }

      // --- respawn on fall ---
      if (playerPos.y < RESPAWN_Y) {
        playerPos.set(spawn.x, spawn.y, spawn.z);
        velocity.set(0, 0, 0);
        updateStatus('Fell off — respawned at start.');
      }

      // --- checkpoint detection ---
      if (runStarted && nextCpIdx < (level.checkpoints ?? []).length) {
        const cp = level.checkpoints[nextCpIdx];
        if (distTo(playerPos, cp) < cp.radius) {
          const tsMs = nowMs();
          _checkpoints.push({ index: cp.index, timestampMs: tsMs });
          cpMarkers[nextCpIdx].material.color.set('#76ff03');
          cpMarkers[nextCpIdx].material.opacity = 0.4;
          nextCpIdx++;
          const total = level.checkpoints.length;
          updateStatus(`Checkpoint ${nextCpIdx}/${total} reached!`);
        }
      }

      // --- finish detection ---
      if (
        runStarted &&
        !_runFinished &&
        nextCpIdx >= (level.checkpoints ?? []).length &&
        finishDef &&
        distTo(playerPos, finishDef) < finishDef.radius
      ) {
        _runFinished = true;
        const finishMs = nowMs();
        const totalMs  = finishMs - _runStartMs;
        const secs     = (totalMs / 1000).toFixed(2);
        controls.unlock();
        updateStatus(`Finish! Time: ${secs}s — submitting run-summary…`);
        submitRunSummary(matchId, finishMs);
      }

      camera.position.copy(playerPos);
    }

    // Animate finish marker
    if (finishMarker && !_runFinished) {
      finishMarker.rotation.y += dt * 1.5;
    }

    renderer.render(scene, camera);
  }

  animate();
  updateStatus('Click the game canvas to lock pointer and start. WASD to move, Space to jump.');
}

// -------------------------------------------------------------------------
// Bootstrap — wires the "Play as Guest" button and exports initGame
// -------------------------------------------------------------------------
document.addEventListener('DOMContentLoaded', () => {
  const btnGuest    = document.getElementById('btn-guest');
  const authScreen  = document.getElementById('auth-screen');   // Bug A fix: hide the whole screen
  const gameSection = document.getElementById('game-section');
  const authError   = document.getElementById('auth-error');

  if (!btnGuest) return;  // page not loaded yet (shouldn't happen for DOMContentLoaded)

  btnGuest.addEventListener('click', async () => {
    btnGuest.disabled = true;
    authError.textContent = '';
    try {
      await guestSignIn();

      // Bug A fix: hide #auth-screen (the full overlay), not just #auth-panel.
      // Hiding only #auth-panel left the screen background visible, blocking the canvas.
      if (authScreen) authScreen.classList.add('hidden');
      if (gameSection) gameSection.classList.remove('hidden');

      // Trigger lobby flow after sign-in (lobby.js hooks into lobby panel buttons).
      // lobby.js fires initLobbyControls() which will call window.startGame(sessionId)
      // when a match is formed. The match-id-input bypass for direct WS testing is
      // handled inside lobby.js (it checks the field on Create/Join).
      if (typeof window.initLobbyControls === 'function') {
        window.initLobbyControls(() => _accessToken);
      }

      // Direct match-id bypass: if a UUID is typed in the test input, start immediately.
      const matchIdEl = document.getElementById('match-id-input');
      const matchId   = matchIdEl?.value?.trim() || null;
      if (matchId) {
        await initGame(matchId);
      }
      // Otherwise game starts when lobby.js resolves the match and calls window.startGame().
    } catch (err) {
      authError.textContent = err.message ?? 'Guest sign-in failed.';
      btnGuest.disabled = false;
    }
  });

  // Expose initGame as window.startGame so lobby.js can call it after match is found.
  window.startGame = initGame;
});
