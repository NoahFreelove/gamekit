/* GameKit Admin — fake data shaped like a real ASP.NET Core app would expose. */

const NOW = new Date('2026-04-26T14:32:00Z').getTime();
const ago = (mins) => new Date(NOW - mins * 60000);

const fmtRel = (d) => {
  const diff = (NOW - d.getTime()) / 1000;
  if (diff < 60) return Math.floor(diff) + 's ago';
  if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
  if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
  return Math.floor(diff / 86400) + 'd ago';
};
const fmtAbs = (d) => {
  const z = (n) => String(n).padStart(2, '0');
  return `${d.getUTCFullYear()}-${z(d.getUTCMonth()+1)}-${z(d.getUTCDate())} ${z(d.getUTCHours())}:${z(d.getUTCMinutes())} UTC`;
};

const PLAYERS = [
  { id: '8f3a2c91-4e5b-4f1c-9d2e-7a8b9c0d1e2f', display: 'NebulaKnight', provider: 'steam', extId: '76561198044567823', country: 'DE', joined: '2024-09-12', lastSeen: ago(4), banned: false, rank: 1842, tier: 'Diamond II', matches: 1429, mmr: 1842 },
  { id: '2b1d4e6f-8c9a-4b3d-9e5f-1a2b3c4d5e6f', display: 'pixelbutter', provider: 'epic', extId: 'epic_3f2c8a91d4b6', country: 'US', joined: '2025-01-04', lastSeen: ago(127), banned: true, banReason: 'Confirmed wallhack via anti-cheat report #4421. Three matches with violations in last 24h.', bannedBy: 'maria.alvarez', bannedAt: ago(186), rank: 991, tier: 'Platinum I', matches: 612, mmr: 1410 },
  { id: '5c7e8f01-2d3b-4a5c-8e9f-0a1b2c3d4e5f', display: 'gh0st_io', provider: 'discord', extId: '284819273645871104', country: 'BR', joined: '2024-03-22', lastSeen: ago(11), banned: false, rank: 412, tier: 'Champion', matches: 3187, mmr: 2104 },
  { id: '9d2f1a3b-5c4e-4f6d-8a7b-9c0d1e2f3a4b', display: 'TacticalKettle', provider: 'steam', extId: '76561198099112340', country: 'GB', joined: '2025-03-18', lastSeen: ago(1442), banned: false, rank: 5821, tier: 'Gold III', matches: 84, mmr: 1102 },
  { id: '1a8b7c6d-9e0f-4a1b-8c2d-3e4f5a6b7c8d', display: 'sub_zero_99', provider: 'steam', extId: '76561198033445566', country: 'JP', joined: '2023-11-30', lastSeen: ago(3), banned: false, rank: 73, tier: 'Champion', matches: 5402, mmr: 2287 },
  { id: '4f5e6d7c-8b9a-4f3e-9d2c-1b0a9c8d7e6f', display: 'Astralwave', provider: 'epic', extId: 'epic_d8b1c2f7a3e4', country: 'CA', joined: '2025-02-08', lastSeen: ago(58), banned: false, rank: 2247, tier: 'Diamond III', matches: 891, mmr: 1755 },
  { id: '7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d', display: 'Mr_Toaster', provider: 'discord', extId: '481829374651928374', country: 'FR', joined: '2024-07-19', lastSeen: ago(22), banned: false, rank: 3092, tier: 'Diamond I', matches: 421, mmr: 1611 },
  { id: '3c4d5e6f-7a8b-4c9d-1e2f-3a4b5c6d7e8f', display: 'queen_of_lag', provider: 'steam', extId: '76561198077889900', country: 'AU', joined: '2024-12-01', lastSeen: ago(8), banned: true, banReason: 'Verbal abuse, repeated reports. 7 day cooldown.', bannedBy: 'james.chen', bannedAt: ago(420), rank: 4198, tier: 'Platinum III', matches: 233, mmr: 1298 },
  { id: '6f7a8b9c-0d1e-4f2a-3b4c-5d6e7f8a9b0c', display: 'mothball', provider: 'steam', extId: '76561198011223344', country: 'NL', joined: '2025-04-10', lastSeen: ago(2), banned: false, rank: 6822, tier: 'Silver II', matches: 41, mmr: 921 },
  { id: 'b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e', display: 'kotonohaaa', provider: 'epic', extId: 'epic_a7f3c1d8e2b4', country: 'KR', joined: '2024-05-14', lastSeen: ago(45), banned: false, rank: 188, tier: 'Champion', matches: 4123, mmr: 2198 },
  { id: 'a3b4c5d6-e7f8-4a9b-1c2d-3e4f5a6b7c8d', display: 'velour_fog', provider: 'discord', extId: '739182645102837465', country: 'SE', joined: '2024-08-21', lastSeen: ago(290), banned: false, rank: 1422, tier: 'Diamond II', matches: 1188, mmr: 1882 },
  { id: 'd5e6f7a8-b9c0-4d1e-2f3a-4b5c6d7e8f9a', display: 'cap_obvious', provider: 'steam', extId: '76561198055667788', country: 'US', joined: '2025-02-28', lastSeen: ago(70), banned: false, rank: 3451, tier: 'Diamond I', matches: 302, mmr: 1574 },
];

const ACTIVE_PLAYER = PLAYERS[1]; // pixelbutter, banned

const PLAYER_IDENTITIES = [
  { provider: 'epic',     extId: 'epic_3f2c8a91d4b6',     verified: true,  linkedAt: ago(60*24*112), primary: true },
  { provider: 'discord',  extId: '482917364502837461',    verified: true,  linkedAt: ago(60*24*97),  primary: false },
  { provider: 'steam',    extId: '76561198044567823',     verified: false, linkedAt: ago(60*24*38),  primary: false },
];

const PLAYER_MATCHES = [
  { id: 'm_8a7b6c5d4e3f', mode: 'Ranked 3v3', pool: 'eu-west', result: 'Loss', score: '1-3', mmrΔ: -18, dur: '7m 42s', ended: ago(186) },
  { id: 'm_4f3e2d1c0b9a', mode: 'Ranked 3v3', pool: 'eu-west', result: 'Win',  score: '3-2', mmrΔ: +14, dur: '11m 03s', ended: ago(220) },
  { id: 'm_b9c8d7e6f5a4', mode: 'Ranked 3v3', pool: 'eu-west', result: 'Win',  score: '3-0', mmrΔ: +16, dur: '6m 28s', ended: ago(241) },
  { id: 'm_2c3d4e5f6a7b', mode: 'Casual',     pool: 'eu-west', result: 'Loss', score: '0-3', mmrΔ:   0, dur: '5m 12s', ended: ago(290) },
  { id: 'm_9f8e7d6c5b4a', mode: 'Ranked 3v3', pool: 'eu-west', result: 'Loss', score: '2-3', mmrΔ: -12, dur: '13m 48s', ended: ago(312) },
  { id: 'm_1b2c3d4e5f6a', mode: 'Ranked 1v1', pool: 'eu-west', result: 'Win',  score: '1-0', mmrΔ: +9,  dur: '4m 02s', ended: ago(360) },
];

const PLAYER_CREDS = [
  { kind: 'password', addedAt: ago(60*24*112), lastUsed: ago(186), strength: 'Strong (Argon2id)' },
  { kind: 'totp',     addedAt: ago(60*24*94),  lastUsed: ago(220), label: 'Authy · iPhone 15' },
  { kind: 'recovery', addedAt: ago(60*24*94),  lastUsed: null,     label: '8 codes · 2 used' },
];

const ADMINS = [
  { id: 'a_001', user: 'maria.alvarez',  role: 'superadmin', email: 'maria.alvarez@studio.dev',  lastSeen: ago(2),    twoFA: true },
  { id: 'a_002', user: 'james.chen',     role: 'admin',      email: 'james.chen@studio.dev',     lastSeen: ago(28),   twoFA: true },
  { id: 'a_003', user: 'priya.kapoor',   role: 'admin',      email: 'priya.kapoor@studio.dev',   lastSeen: ago(94),   twoFA: true },
  { id: 'a_004', user: 'devon.okafor',   role: 'admin',      email: 'devon.okafor@studio.dev',   lastSeen: ago(360),  twoFA: false },
  { id: 'a_005', user: 'sam.lindqvist',  role: 'superadmin', email: 'sam.lindqvist@studio.dev',  lastSeen: ago(15),   twoFA: true },
  { id: 'a_006', user: 'rikki.tanaka',   role: 'admin',      email: 'rikki.tanaka@studio.dev',   lastSeen: ago(1244), twoFA: true },
];

const AUDIT = [
  {
    id: 'ev_91823', when: ago(2), actor: 'maria.alvarez', namespace: 'players',
    action: 'ban', target: 'pixelbutter', targetId: '2b1d4e6f-8c9a-4b3d-9e5f-1a2b3c4d5e6f',
    sentence: ['ban', 'maria.alvarez', 'banned', 'pixelbutter'],
    reason: 'Confirmed wallhack via anti-cheat report #4421. Three matches with violations in last 24h.',
    diff: [
      { field: 'banned',    before: false, after: true },
      { field: 'banReason', before: null,  after: '"Confirmed wallhack…"' },
      { field: 'bannedBy',  before: null,  after: '"maria.alvarez"' },
    ],
  },
  {
    id: 'ev_91822', when: ago(8), actor: 'system', namespace: 'matchmaking',
    action: 'pool.scale', target: 'eu-west',
    sentence: ['pool.scale', 'system', 'scaled queue', 'eu-west', 'from 4 to 6 workers'],
    diff: [
      { field: 'workers', before: 4, after: 6 },
      { field: 'reason',  before: null, after: '"queue depth > 250 for 60s"' },
    ],
  },
  {
    id: 'ev_91821', when: ago(14), actor: 'james.chen', namespace: 'rankings',
    action: 'rank.adjust', target: 'TacticalKettle',
    sentence: ['rank.adjust', 'james.chen', 'adjusted rank for', 'TacticalKettle'],
    reason: 'Smurf detection - account misranked after 4 placements.',
    diff: [
      { field: 'mmr',  before: 1102, after: 1480 },
      { field: 'tier', before: '"Gold III"', after: '"Diamond III"' },
    ],
  },
  {
    id: 'ev_91820', when: ago(42), actor: 'priya.kapoor', namespace: 'players',
    action: 'unban', target: 'velour_fog',
    sentence: ['unban', 'priya.kapoor', 'unbanned', 'velour_fog'],
    diff: [
      { field: 'banned', before: true, after: false },
      { field: 'banReason', before: '"Toxic chat — 3d cooldown"', after: null },
    ],
  },
  {
    id: 'ev_91819', when: ago(73), actor: 'maria.alvarez', namespace: 'admins',
    action: 'admin.create', target: 'rikki.tanaka',
    sentence: ['admin.create', 'maria.alvarez', 'created admin account', 'rikki.tanaka'],
    diff: [
      { field: 'role',  before: null, after: '"admin"' },
      { field: 'email', before: null, after: '"rikki.tanaka@studio.dev"' },
    ],
  },
  {
    id: 'ev_91818', when: ago(110), actor: 'james.chen', namespace: 'players',
    action: 'ban', target: 'queen_of_lag',
    sentence: ['ban', 'james.chen', 'banned', 'queen_of_lag'],
    reason: 'Verbal abuse, repeated reports. 7 day cooldown.',
    diff: [
      { field: 'banned',    before: false, after: true },
      { field: 'banReason', before: null,  after: '"Verbal abuse…"' },
      { field: 'expiresAt', before: null,  after: '"2026-05-03T18:12Z"' },
    ],
  },
  {
    id: 'ev_91817', when: ago(186), actor: 'maria.alvarez', namespace: 'auth',
    action: 'login', target: 'maria.alvarez',
    sentence: ['login', 'maria.alvarez', 'signed in', '203.0.113.42'],
    diff: [
      { field: 'ip',       before: null, after: '"203.0.113.42"' },
      { field: 'userAgent', before: null, after: '"Firefox 124 / Linux"' },
    ],
  },
  {
    id: 'ev_91816', when: ago(241), actor: 'sam.lindqvist', namespace: 'players',
    action: 'gdpr.delete', target: '0c2d8f12-…-anon',
    sentence: ['gdpr.delete', 'sam.lindqvist', 'GDPR-deleted', '0c2d8f12-…-anon'],
    reason: 'Player request via support ticket #SR-8821.',
    diff: [
      { field: 'displayName', before: '"crispykale"', after: '"[redacted]"' },
      { field: 'email',       before: '"k**@gmail.com"', after: 'null' },
      { field: 'identities',  before: '3 records', after: '"[redacted]"' },
    ],
  },
];

// rolling sparkline data
const SPARK_QPS = [42,38,41,52,61,58,49,55,63,71,68,72,80,76,82,88,84,79,90,96,92,97,103,98,102,108,112,109,118,124,120];
const SPARK_LAT = [4.1,4.0,4.2,4.3,4.1,4.0,3.9,4.0,4.2,4.4,4.5,4.3,4.2,4.1,4.0,3.9,4.0,4.1,4.2,4.4,4.6,4.7,4.5,4.4,4.3,4.5,4.7,5.0,4.8,4.6,4.5];

const HEALTH = {
  postgres: { status: 'healthy', value: '4.5', unit: 'ms p99', detail: 'pool 14/40 · 1.2k qps', strip: Array.from({length:30},()=>1) },
  redis:    { status: 'healthy', value: '0.9', unit: 'ms p99', detail: '6/16 conns · 18.4k ops/s', strip: Array.from({length:30},()=>1) },
  errorRate:{ status: 'degraded',value: '0.42', unit: '% / 5m', detail: '127 errors over last 5m · spike at 14:28Z', strip: (() => { const a=Array.from({length:30},()=>1); a[24]='d'; a[25]='d'; a[26]='x'; return a; })() },
};

const QUEUES = [
  { pool: 'na-east',  depth: 47,  wait: '12s', workers: 4, status: 'healthy' },
  { pool: 'na-west',  depth: 31,  wait: '9s',  workers: 3, status: 'healthy' },
  { pool: 'eu-west',  depth: 268, wait: '38s', workers: 6, status: 'degraded' },
  { pool: 'eu-east',  depth: 88,  wait: '22s', workers: 4, status: 'healthy' },
  { pool: 'ap-south', depth: 14,  wait: '7s',  workers: 2, status: 'healthy' },
  { pool: 'sa-east',  depth: 22,  wait: '11s', workers: 2, status: 'healthy' },
];

window.GK = {
  NOW, ago, fmtRel, fmtAbs,
  PLAYERS, ACTIVE_PLAYER,
  PLAYER_IDENTITIES, PLAYER_MATCHES, PLAYER_CREDS,
  ADMINS, AUDIT,
  SPARK_QPS, SPARK_LAT, HEALTH, QUEUES,
};
