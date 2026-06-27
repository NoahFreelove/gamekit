// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// Two-player browser e2e for the Platformer3D demo (headless chromium via Playwright).
// Drives TWO isolated browser contexts (= two players) through the real client UI:
//   1. sign-in lands on the MENU (not auto-solo)
//   2. Ranked Match pairs the two players into one game
//   3. finishing shows the results screen with win/loss + a rating delta
//   4. the leaderboard renders with real (non-zero) ratings
//   5. a friend party: both ready → an UNRANKED match starts (no rating change)
//
// Prereqs (one-time):
//   npx playwright@latest install chromium   # downloads chrome-headless-shell to ~/.cache/ms-playwright
//   npm i playwright-core                     # the JS driver (no extra browser download)
// Run (with the demo stack up on :8080):
//   docker compose -f samples/Platformer3D/docker-compose.yml up -d --build
//   node tests/e2e-browser.mjs
// Snap chromium can't sandbox here; Playwright's headless-shell + --no-sandbox works.
import { chromium } from 'playwright-core';
import { readdirSync } from 'fs';
import os from 'os';

const BASE = 'http://localhost:8080';
const base = os.homedir() + '/.cache/ms-playwright';
const dir = readdirSync(base).find(d => d.startsWith('chromium_headless_shell'));
const EXE = `${base}/${dir}/chrome-headless-shell-linux64/chrome-headless-shell`;

let failures = 0;
const ok = (m) => console.log(`  ✓ ${m}`);
const bad = (m) => { console.log(`  ✗ ${m}`); failures++; };
function assert(c, m) { c ? ok(m) : bad(m); }

const activeScreen = (page) => page.$$eval('.screen.active', els => els.map(e => e.id.replace('screen-','')));
async function waitScreen(page, name, timeout = 40000) {
  await page.waitForSelector(`#screen-${name}.active`, { timeout });
}
async function signIn(page, label) {
  await page.goto(BASE, { waitUntil: 'domcontentloaded', timeout: 20000 });
  await page.waitForSelector('#btn-guest', { timeout: 15000 });
  await page.click('#btn-guest');
  await waitScreen(page, 'menu', 20000);
  console.log(`  [${label}] signed in → menu`);
}
async function finishRun(page, ms) {
  await page.waitForFunction(() => typeof window.__debugFinish === 'function', { timeout: 20000 });
  await page.evaluate((m) => window.__debugFinish(m), ms);
}

const browser = await chromium.launch({ executablePath: EXE, args: ['--no-sandbox', '--disable-gpu'] });
const ctxA = await browser.newContext();
const ctxB = await browser.newContext();
const A = await ctxA.newPage();
const B = await ctxB.newPage();
A.on('pageerror', e => console.log('  [A pageerror]', e.message));
B.on('pageerror', e => console.log('  [B pageerror]', e.message));

try {
  // ── 1. Sign in → MENU (not auto-solo) ──────────────────────────────────────
  console.log('\n=== 1. Sign-in lands on the MENU (not auto-solo) ===');
  await signIn(A, 'A');
  await signIn(B, 'B');
  assert((await activeScreen(A)).includes('menu'), 'A on menu after sign-in');
  assert(!(await activeScreen(A)).includes('game'), 'A NOT dropped straight into solo game');

  // ── 2. Ranked quick-match pairs the two players ────────────────────────────
  console.log('\n=== 2. Ranked Match pairs two players into one game ===');
  await A.click('#btn-ranked');
  await B.click('#btn-ranked');
  await waitScreen(A, 'searching', 10000); ok('A entered matchmaking queue');
  await Promise.all([waitScreen(A, 'game'), waitScreen(B, 'game')]);
  ok('both players matched → in game');

  // ── 3. Finish → results with win/loss + rating delta ───────────────────────
  console.log('\n=== 3. Finish → results screen (win/loss + rating delta) ===');
  await finishRun(A, 7000);  // A faster → A wins
  await finishRun(B, 9000);  // B slower → B loses
  await Promise.all([waitScreen(A, 'results', 30000), waitScreen(B, 'results', 30000)]);
  ok('both reached the results screen');

  // Wait for BOTH players' rating deltas to populate (session completes, then rating applies
  // in ~5s, polled by the client). Both must finalise out of the "waiting for opponent" state.
  const ratingShown = (p) => p.waitForFunction(() => document.querySelector('#result-rating')?.textContent?.includes('→'), { timeout: 35000 });
  await Promise.all([ratingShown(A).catch(()=>{}), ratingShown(B).catch(()=>{})]);
  const aBanner = (await A.textContent('#result-banner'))?.trim();
  const bBanner = (await B.textContent('#result-banner'))?.trim();
  const aRating = (await A.textContent('#result-rating'))?.trim();
  const bRating = (await B.textContent('#result-rating'))?.trim();
  console.log(`  A banner="${aBanner}" rating="${aRating}"`);
  console.log(`  B banner="${bBanner}" rating="${bRating}"`);
  assert(/victory/i.test(aBanner), 'A sees Victory (faster time won)');
  assert(/defeat/i.test(bBanner), 'B sees Defeat');
  assert(/→/.test(aRating) && /\(\+?\-?\d+\)/.test(aRating), 'A sees a rating delta (e.g. 1000 → 10xx)');
  assert(/→/.test(bRating), 'B sees a rating delta');

  // ── 4. Leaderboard renders with both players ───────────────────────────────
  console.log('\n=== 4. Leaderboard renders ===');
  await A.click('#btn-results-board');
  await waitScreen(A, 'leaderboard', 10000);
  await A.waitForSelector('#screen-leaderboard table.lb tbody tr', { timeout: 15000 }).catch(()=>{});
  const lbRows = await A.$$eval('#screen-leaderboard table.lb tbody tr', rs => rs.length);
  // Rating is the 3rd cell (index 2). After a ranked match it MUST be non-zero (the bug:
  // placement-hiding made it show 0).
  const ratings = await A.$$eval('#screen-leaderboard table.lb tbody tr', rs =>
    rs.map(r => parseInt(r.children[2]?.textContent || '0', 10)));
  console.log(`  leaderboard rows: ${lbRows}, ratings: ${JSON.stringify(ratings)}`);
  assert(lbRows >= 1, 'leaderboard shows ranked players');
  assert(ratings.some(v => v > 0), 'leaderboard shows real (non-zero) ratings');

  // ── 5. Friend party (unranked) — "ready" actually starts a match ───────────
  console.log('\n=== 5. Friend party: both ready → match starts (unranked) ===');
  await A.click('#btn-lb-back'); await waitScreen(A, 'menu', 10000);
  await A.click('#btn-party'); await waitScreen(A, 'party', 10000);
  await A.click('#btn-create-party');
  await A.waitForSelector('#party-code:not(:empty)', { timeout: 10000 });
  const code = (await A.textContent('#party-code'))?.trim();
  console.log(`  A created party, code=${code?.slice(0,8)}…`);
  assert(!!code, 'A got an invite code');

  await B.click('#btn-results-again').catch(()=>{}); // B back to menu
  await waitScreen(B, 'menu', 10000);
  await B.click('#btn-party'); await waitScreen(B, 'party', 10000);
  await B.fill('#invite-input', code);
  await B.click('#btn-join-party');

  // Both should reach ReadyChecking → Ready button enabled.
  await Promise.all([
    A.waitForSelector('#btn-ready:not([disabled])', { timeout: 15000 }),
    B.waitForSelector('#btn-ready:not([disabled])', { timeout: 15000 }),
  ]);
  ok('both party members can Ready');
  await A.click('#btn-ready');
  await B.click('#btn-ready');
  await Promise.all([waitScreen(A, 'game', 30000), waitScreen(B, 'game', 30000)]);
  ok('both ready → match started (the flow that previously "did nothing")');

  await finishRun(A, 8000);
  await finishRun(B, 10000);
  await Promise.all([waitScreen(A, 'results', 30000), waitScreen(B, 'results', 30000)]);
  // Wait for the friendly match to fully resolve (out of the "waiting for opponent" pending state).
  await A.waitForFunction(() => /unranked|no rating/i.test(document.querySelector('#result-note')?.textContent || ''), { timeout: 30000 }).catch(() => {});
  const aNote = (await A.textContent('#result-note'))?.trim();
  const aBanner2 = (await A.textContent('#result-banner'))?.trim();
  console.log(`  A party-result banner="${aBanner2}" note="${aNote}"`);
  assert(/unranked|no rating/i.test(aNote || ''), 'friendly match reported as unranked');
  // And confirm the friendly match did NOT change rating (still no rating delta shown).
  const aRatingEl = (await A.textContent('#result-rating'))?.trim();
  assert(!/→/.test(aRatingEl || ''), 'friendly match shows no rating change');

} catch (e) {
  bad('UNCAUGHT: ' + e.message);
  console.log(e.stack);
} finally {
  await browser.close();
}

console.log(`\n═══ e2e result: ${failures === 0 ? 'ALL PASSED' : failures + ' FAILURE(S)'} ═══`);
process.exit(failures === 0 ? 0 : 1);
