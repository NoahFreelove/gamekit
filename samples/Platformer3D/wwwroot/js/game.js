// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// game.js — Platformer3D three.js engine + auth (ES module, no bundler).
// Exposes guestSignIn/authFetch/getAccessToken/getPlayerId and runGame({sessionId,mode,onFinish}).
// The flow controller (app.js) owns screens; this module owns only auth + the 3D run.

import * as THREE from '/js/three.module.js';
import { PointerLockControls } from '/js/addons/PointerLockControls.js';

// ─── Auth state ────────────────────────────────────────────────────────────
let _accessToken = null;
let _refreshToken = null;
let _playerId = null;
const KEY_REFRESH = 'gk.refresh_token';
const KEY_DEVICE  = 'gk.device_id';

function getOrCreateDeviceId() {
  let id = localStorage.getItem(KEY_DEVICE);
  if (!id) { id = crypto.randomUUID(); localStorage.setItem(KEY_DEVICE, id); }
  return id;
}

function decodeJwtPayload(jwt) {
  try {
    const b64 = jwt.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(b64));
  } catch { return null; }
}

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
  const payload = decodeJwtPayload(_accessToken);
  _playerId = payload?.sub ?? payload?.nameid ?? null;
  return payload;
}

export function getAccessToken() { return _accessToken; }
export function getPlayerId() { return _playerId; }

export async function authFetch(url, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  headers['X-GameKit-Device'] = getOrCreateDeviceId();
  if (_accessToken) headers['Authorization'] = 'Bearer ' + _accessToken;
  let resp = await fetch(url, { ...opts, headers });
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
        const payload = decodeJwtPayload(_accessToken);
        _playerId = payload?.sub ?? payload?.nameid ?? _playerId;
        headers['Authorization'] = 'Bearer ' + _accessToken;
        resp = await fetch(url, { ...opts, headers });
      }
    }
  }
  return resp;
}

// ─── Timing (D-02: integer-ms) ───────────────────────────────────────────────
function nowMs() { return Math.trunc(performance.now() + performance.timeOrigin); }

// ─── Geometry helpers ────────────────────────────────────────────────────────
function buildPlatformMesh(p) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(p.w, p.h, p.d), new THREE.MeshLambertMaterial({ color: p.color }));
  mesh.position.set(p.x, p.y - p.h / 2, p.z);
  mesh.receiveShadow = true; mesh.castShadow = true;
  return mesh;
}
function buildCheckpointMarker(cp) {
  const mesh = new THREE.Mesh(new THREE.SphereGeometry(0.5, 16, 8), new THREE.MeshLambertMaterial({ color: cp.color, transparent: true, opacity: 0.8 }));
  mesh.position.set(cp.x, cp.y, cp.z);
  return mesh;
}
function buildFinishMarker(f) {
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(f.radius * 0.3, f.radius * 0.3, 2, 16), new THREE.MeshLambertMaterial({ color: f.color, transparent: true, opacity: 0.9 }));
  mesh.position.set(f.x, f.y, f.z);
  return mesh;
}
function playerOnPlatform(playerPos, platform) {
  const hw = platform.w / 2, hd = platform.d / 2, surfaceY = platform.y;
  if (playerPos.x >= platform.x - hw - 0.5 && playerPos.x <= platform.x + hw + 0.5 &&
      playerPos.z >= platform.z - hd - 0.5 && playerPos.z <= platform.z + hd + 0.5) {
    const footY = playerPos.y - 0.9;
    if (footY <= surfaceY + 0.15 && footY >= surfaceY - 0.5) return surfaceY;
  }
  return null;
}
function distTo(a, b) { const dx=a.x-b.x, dy=a.y-b.y, dz=a.z-b.z; return Math.sqrt(dx*dx+dy*dy+dz*dz); }

function fmtSecs(ms) { return (ms / 1000).toFixed(2); }
function setStatus(msg) { const el = document.getElementById('game-status'); if (el) el.textContent = msg; }

// ─── WebSocket run-summary submission (matches only) ─────────────────────────
// CRITICAL: browsers cannot set an Authorization header on a WS upgrade, so the JWT is
// passed as ?access_token=<JWT>. The host extracts it for /ws/game (see Program.cs).
function submitRunSummary(sessionId, runStartMs, checkpoints, finishMs, cb) {
  const wsProto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const url = `${wsProto}//${location.host}/ws/game/${sessionId}?access_token=${encodeURIComponent(_accessToken ?? '')}`;
  let ws;
  try { ws = new WebSocket(url); }
  catch (e) { cb({ rejected: true, reason: 'ws_open_failed' }); return null; }

  ws.addEventListener('open', () => {
    ws.send(JSON.stringify({ type: 'run_start', matchId: sessionId, startMs: runStartMs }));
    for (const cp of checkpoints) ws.send(JSON.stringify({ type: 'checkpoint', index: cp.index, timestampMs: cp.timestampMs }));
    ws.send(JSON.stringify({ type: 'run_finish', finishMs }));
  });
  ws.addEventListener('message', (ev) => {
    let msg; try { msg = JSON.parse(ev.data); } catch { return; }
    if (msg.type === 'ping') { ws.send(JSON.stringify({ type: 'pong' })); }
    else if (msg.type === 'validated') { cb({ validated: true, sessionId, completionMs: msg.completionMs ?? (finishMs - runStartMs) }); try { ws.close(1000); } catch {} }
    else if (msg.type === 'rejected')  { cb({ rejected: true, reason: msg.reason ?? 'unknown' }); try { ws.close(1000); } catch {} }
  });
  ws.addEventListener('error', () => cb({ rejected: true, reason: 'ws_error' }));
  return ws;
}

// ─── The run ─────────────────────────────────────────────────────────────────
let _disposeActive = null;

/**
 * Start a fresh 3D run on #game-canvas. Disposes any previous run first (no leaked
 * listeners / loops). On finish, calls onFinish:
 *   solo:  { solo:true, timeMs }
 *   match: { validated:true, sessionId, completionMs }  OR  { rejected:true, reason }
 * mode is 'ranked' | 'casual' | 'solo' (badge only).
 */
export async function runGame({ sessionId = null, mode = 'solo', onFinish = () => {} }) {
  if (_disposeActive) { _disposeActive(); _disposeActive = null; }

  const canvas  = document.getElementById('game-canvas');
  const overlay = document.getElementById('game-overlay');
  const ac = new AbortController();
  let running = true;
  let activeWs = null;

  // Mode badge
  const badge = document.getElementById('game-mode-badge');
  if (badge) {
    badge.textContent = mode === 'ranked' ? 'Ranked 1v1' : mode === 'casual' ? 'Friendly 1v1' : 'Solo Practice';
    badge.className = mode === 'ranked' ? '' : mode;
  }

  const level = await (await fetch('/assets/level.json')).json();

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(level.sky ?? '#1a237e');
  scene.fog = new THREE.Fog(level.fog?.color ?? '#1a237e', level.fog?.near ?? 30, level.fog?.far ?? 120);
  const camera = new THREE.PerspectiveCamera(75, canvas.clientWidth / canvas.clientHeight || 1.6, 0.1, 500);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(canvas.clientWidth || window.innerWidth, canvas.clientHeight || window.innerHeight);
  renderer.shadowMap.enabled = true;

  window.addEventListener('resize', () => {
    const w = canvas.clientWidth || window.innerWidth, h = canvas.clientHeight || window.innerHeight;
    camera.aspect = w / h; camera.updateProjectionMatrix(); renderer.setSize(w, h);
  }, { signal: ac.signal });

  scene.add(new THREE.AmbientLight('#ffffff', 0.5));
  const sun = new THREE.DirectionalLight('#ffffff', 1.2);
  sun.position.set(20, 40, 10); sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048);
  scene.add(sun);

  const platforms = level.platforms ?? [];
  for (const p of platforms) scene.add(buildPlatformMesh(p));
  const cpMarkers = [];
  const cpDefs = level.checkpoints ?? [];
  for (const cp of cpDefs) { const m = buildCheckpointMarker(cp); scene.add(m); cpMarkers.push(m); }
  const finishDef = level.finish;
  const finishMarker = finishDef ? buildFinishMarker(finishDef) : null;
  if (finishMarker) scene.add(finishMarker);

  const controls = new PointerLockControls(camera, document.body);
  const spawn = level.spawn ?? { x: 0, y: 1.0, z: 0 };
  const playerPos = new THREE.Vector3(spawn.x, spawn.y, spawn.z);
  const velocity = new THREE.Vector3();
  camera.position.copy(playerPos);

  const GRAVITY = level.gravity ?? -18, JUMP_VEL = level.jumpVelocity ?? 8, MOVE_SPEED = level.moveSpeed ?? 6, RESPAWN_Y = -20;
  const keys = { w:false, a:false, s:false, d:false, space:false };

  let runStarted = false, runFinished = false, runStartMs = 0, nextCpIdx = 0;
  const checkpoints = [];

  document.addEventListener('keydown', (e) => {
    const k = e.code;
    if (k==='KeyW'||k==='ArrowUp') keys.w=true; if (k==='KeyS'||k==='ArrowDown') keys.s=true;
    if (k==='KeyA'||k==='ArrowLeft') keys.a=true; if (k==='KeyD'||k==='ArrowRight') keys.d=true;
    if (k==='Space') { e.preventDefault(); keys.space=true; }
  }, { signal: ac.signal });
  document.addEventListener('keyup', (e) => {
    const k = e.code;
    if (k==='KeyW'||k==='ArrowUp') keys.w=false; if (k==='KeyS'||k==='ArrowDown') keys.s=false;
    if (k==='KeyA'||k==='ArrowLeft') keys.a=false; if (k==='KeyD'||k==='ArrowRight') keys.d=false;
    if (k==='Space') keys.space=false;
  }, { signal: ac.signal });

  canvas.addEventListener('click', () => { if (!runFinished) controls.lock(); }, { signal: ac.signal });
  controls.addEventListener('lock',   () => overlay?.classList.add('hidden'), { signal: ac.signal });
  controls.addEventListener('unlock', () => { if (!runFinished) overlay?.classList.remove('hidden'); }, { signal: ac.signal });

  const timerEl = document.getElementById('game-timer');

  function finishRun(finishMs) {
    if (runFinished) return;
    runFinished = true;
    try { controls.unlock(); } catch {}
    const timeMs = finishMs - runStartMs;
    if (timerEl) timerEl.textContent = fmtSecs(timeMs);
    if (!sessionId) { setStatus(`Finished in ${fmtSecs(timeMs)}s`); onFinish({ solo: true, timeMs }); return; }
    setStatus(`Finished in ${fmtSecs(timeMs)}s — submitting…`);
    activeWs = submitRunSummary(sessionId, runStartMs, checkpoints, finishMs, (r) => onFinish(r));
  }

  // Deterministic finish for automated tests: synthesize valid (ascending) checkpoints.
  window.__debugFinish = (ms = 8000) => {
    if (runFinished) return;
    runStarted = true;
    runStartMs = nowMs() - ms;
    const finishMs = nowMs();
    checkpoints.length = 0;
    const n = cpDefs.length;
    for (let i = 0; i < n; i++) checkpoints.push({ index: cpDefs[i].index, timestampMs: runStartMs + Math.round(ms * (i + 1) / (n + 1)) });
    finishRun(finishMs);
  };

  const clock = new THREE.Clock();
  function animate() {
    if (!running) return;
    requestAnimationFrame(animate);
    const dt = Math.min(clock.getDelta(), 0.1);

    if (controls.isLocked && !runFinished) {
      if (!runStarted && (keys.w||keys.a||keys.s||keys.d||keys.space)) {
        runStarted = true; runStartMs = nowMs(); setStatus('Go! Reach every checkpoint then the finish.');
      }
      const forward = new THREE.Vector3(); camera.getWorldDirection(forward); forward.y = 0; forward.normalize();
      const right = new THREE.Vector3().crossVectors(forward, new THREE.Vector3(0,1,0)).normalize();
      const move = new THREE.Vector3();
      if (keys.w) move.addScaledVector(forward, MOVE_SPEED);
      if (keys.s) move.addScaledVector(forward, -MOVE_SPEED);
      if (keys.d) move.addScaledVector(right, MOVE_SPEED);
      if (keys.a) move.addScaledVector(right, -MOVE_SPEED);
      playerPos.x += move.x * dt; playerPos.z += move.z * dt;
      velocity.y += GRAVITY * dt; playerPos.y += velocity.y * dt;

      let highest = null;
      for (const p of platforms) { const sy = playerOnPlatform(playerPos, p); if (sy !== null && (highest === null || sy > highest)) highest = sy; }
      let onGround = false;
      if (highest !== null && playerPos.y - 0.9 <= highest + 0.15) { playerPos.y = highest + 0.9; if (velocity.y < 0) velocity.y = 0; onGround = true; }
      if (keys.space && onGround) { velocity.y = JUMP_VEL; onGround = false; }
      if (playerPos.y < RESPAWN_Y) { playerPos.set(spawn.x, spawn.y, spawn.z); velocity.set(0,0,0); setStatus('Fell — respawned.'); }

      if (runStarted && nextCpIdx < cpDefs.length) {
        const cp = cpDefs[nextCpIdx];
        if (distTo(playerPos, cp) < cp.radius) {
          checkpoints.push({ index: cp.index, timestampMs: nowMs() });
          cpMarkers[nextCpIdx].material.color.set('#76ff03'); cpMarkers[nextCpIdx].material.opacity = 0.4;
          nextCpIdx++; setStatus(`Checkpoint ${nextCpIdx}/${cpDefs.length}`);
        }
      }
      if (runStarted && !runFinished && nextCpIdx >= cpDefs.length && finishDef && distTo(playerPos, finishDef) < finishDef.radius) {
        finishRun(nowMs());
      }
      camera.position.copy(playerPos);
      if (timerEl && !runFinished) timerEl.textContent = fmtSecs(runStarted ? nowMs() - runStartMs : 0);
    }
    if (finishMarker && !runFinished) finishMarker.rotation.y += dt * 1.5;
    renderer.render(scene, camera);
  }

  // Reset transient UI
  if (overlay) overlay.classList.remove('hidden');
  if (timerEl) timerEl.textContent = '0.00';
  setStatus('Click to capture the mouse, then move to start.');
  animate();

  _disposeActive = function dispose() {
    running = false;
    ac.abort();
    try { controls.unlock(); } catch {}
    try { if (activeWs && activeWs.readyState <= 1) activeWs.close(1000); } catch {}
    try { renderer.dispose(); } catch {}
    delete window.__debugFinish;
  };
}

export function disposeGame() { if (_disposeActive) { _disposeActive(); _disposeActive = null; } }
