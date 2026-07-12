<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# JWT signing key management

`GameKit.Auth` issues short-lived RSA-signed JWTs for player access tokens. This
doc explains how to generate, deploy, store, and rotate the signing keys for
production — and what the dev scripts do not cover.

---

## Algorithm + key shape

GameKit issues JWTs signed with **RS256** (RSA + SHA-256). The recommended key
size is **2048 bits**. RSA was chosen over EC for symmetry between v1's small
operator footprint (every customer can generate RSA with `openssl`) and the lack
of ECDSA-specific demand. EC keys are not currently supported — if you ship `EC`
PEM files, GameKit will fail at `IHost.StartAsync` with a key-load exception.

The keys are referenced from configuration as filesystem paths:

| Option key                                | Purpose                                                          |
|-------------------------------------------|------------------------------------------------------------------|
| `GameKit:Auth:Jwt:SigningKeyPath`         | Private PEM — used by the issuance pipeline                       |
| `GameKit:Auth:Jwt:ValidationKeyPath`      | Public PEM — used by the JWT bearer middleware to validate        |
| `GameKit:Auth:Jwt:Kid`                    | Key identifier — appears in JWT header `kid` claim; required for rotation |
| `GameKit:Auth:Jwt:Issuer`                 | `iss` claim — typically your service's public URL                 |
| `GameKit:Auth:Jwt:Audience`               | `aud` claim — typically the same as `Issuer` for single-audience deployments |

The same `kid` value must travel with the public key, so the validator knows which
key issued each token during rotation.

---

## Dev key generation

For a developer machine, the shipped `scripts/gen-test-rsa-pem.sh` produces a
throwaway pair in `samples/TicTacToeDuel/keys/`:

```bash
./scripts/gen-test-rsa-pem.sh
```

What it does (read the script directly — it is 22 lines):

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-priv.pem
openssl rsa -in dev-priv.pem -pubout -out dev-pub.pem
chmod 0600 dev-priv.pem
chmod 0644 dev-pub.pem
```

The resulting keys are **dev-only** — `samples/TicTacToeDuel/keys/README.md`
spells this out. Do NOT promote them past your laptop.

---

## Production key generation

Generate fresh keys per deployment, on a host you control:

```bash
DEPLOY_KID="prod-$(date -u +%Y-%m-%d)-rsa2048"
KEY_DIR=/srv/mygame/keys
mkdir -p "$KEY_DIR"

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 \
    -out "$KEY_DIR/${DEPLOY_KID}-priv.pem"
openssl rsa -in "$KEY_DIR/${DEPLOY_KID}-priv.pem" -pubout \
    -out "$KEY_DIR/${DEPLOY_KID}-pub.pem"

chmod 0600 "$KEY_DIR/${DEPLOY_KID}-priv.pem"
chmod 0644 "$KEY_DIR/${DEPLOY_KID}-pub.pem"
chown mygame:mygame "$KEY_DIR"/${DEPLOY_KID}-*.pem
```

Embed the `kid` in the filename. The `Kid` configuration value then matches the
key file basename:

```ini
# /etc/mygame/env
GameKit__Auth__Jwt__SigningKeyPath=/srv/mygame/keys/prod-2026-05-25-rsa2048-priv.pem
GameKit__Auth__Jwt__ValidationKeyPath=/srv/mygame/keys/prod-2026-05-25-rsa2048-pub.pem
GameKit__Auth__Jwt__Kid=prod-2026-05-25-rsa2048
GameKit__Auth__Jwt__Issuer=https://mygame.example.com
GameKit__Auth__Jwt__Audience=https://mygame.example.com
```

---

## Storage hardening

Private keys are the single biggest crown-jewel asset in the deployment — anyone
holding the private key can mint indefinite-lifetime JWTs for any player. Treat
them as such:

| Property                         | Required value                                      | Why                                                       |
|----------------------------------|-----------------------------------------------------|-----------------------------------------------------------|
| File mode                        | `0600` (owner read+write only)                       | Any group/world read leaks the key to other local users   |
| Owner                            | The app's runtime user (`mygame`), NOT `root`        | Process should not need privilege escalation to read it   |
| Filesystem                       | Local disk or encrypted volume (LUKS / dm-crypt)     | Off-host NFS/CIFS may transit the key in plaintext         |
| Backups                          | Encrypted at rest; restricted to break-glass admins  | Backup tapes are a common exfil vector                    |
| Git                              | NEVER                                                | Once a key lands in git history, treat it as compromised   |
| Logs                             | NEVER                                                | A startup log line like `Loaded key: -----BEGIN RSA...`    |

The `.gitignore` in `samples/TicTacToeDuel/keys/` already excludes `*.pem` files.
Mirror that in your own repo:

```
# .gitignore — wherever your deploy artifacts live
*.pem
keys/
secrets/
```

### HSM / KMS (advanced)

For deployments where the threat model includes host-level compromise (a single
machine getting popped should not also leak the JWT key for the entire fleet),
the production deploy can move the private key behind:

- **PKCS#11 HSM** (YubiHSM 2, SoftHSM for testing, Thales Luna for serious
  hardware) — `openssl` can generate the keypair on-device and never expose the
  private bytes to the OS.
- **Cloud KMS with key wrapping** (AWS KMS, GCP KMS) — load an envelope-encrypted
  key at boot; decrypt via KMS API. This breaks the "no cloud-service dependency"
  constraint in `CLAUDE.md` for the auth layer; consider whether you accept that
  trade-off (most operators do not).

Neither path is currently wired into `GameKit.Auth` directly — both require a
custom `IJwtKeyProvider` implementation in your consumer app. The v1 default loads
keys from disk via the `SigningKeyPath` / `ValidationKeyPath` options described
above.

---

## Key rotation

The rotation flow is "issue new + accept both + retire old", spread across the
refresh-token lifetime (default 30 days) so no live session is invalidated.

### Step 0 — pre-rotate planning

| Question                                        | Answer (default GameKit setup)                           |
|-------------------------------------------------|----------------------------------------------------------|
| What is my access-token TTL?                    | 15 minutes (`Jwt.AccessTokenLifetime`)                   |
| What is my refresh-token TTL?                   | 30 days (`Auth.RefreshTokenLifetime`)                    |
| What is the grace period for accepting the OLD key? | At least the refresh-token TTL — 30 days                 |
| What is my deploy cadence?                      | Plan rotations on a >30 day cycle so each rotation lands cleanly with the previous one fully retired |

### Step 1 — generate the new key

```bash
NEW_KID="prod-$(date -u +%Y-%m-%d)-rsa2048"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 \
    -out /srv/mygame/keys/${NEW_KID}-priv.pem
openssl rsa -in /srv/mygame/keys/${NEW_KID}-priv.pem -pubout \
    -out /srv/mygame/keys/${NEW_KID}-pub.pem
chmod 0600 /srv/mygame/keys/${NEW_KID}-priv.pem
chmod 0644 /srv/mygame/keys/${NEW_KID}-pub.pem
chown mygame:mygame /srv/mygame/keys/${NEW_KID}-*.pem
```

### Step 2 — deploy with BOTH keys accepted (issue with new, validate with both)

Your consumer app needs to register the previous key as an additional validator.
The shipped `JwtBearerOptions` exposes `IssuerSigningKeys` (collection) for this
exact purpose:

```csharp
// Program.cs — after AddAuth(...), wire the JWT bearer events to load both keys.
services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwtOpts =>
{
    var oldPub = LoadRsaPublicKey("/srv/mygame/keys/prod-2026-04-25-rsa2048-pub.pem");
    var newPub = LoadRsaPublicKey("/srv/mygame/keys/prod-2026-05-25-rsa2048-pub.pem");
    jwtOpts.TokenValidationParameters.IssuerSigningKeys = new[]
    {
        new RsaSecurityKey(oldPub) { KeyId = "prod-2026-04-25-rsa2048" },
        new RsaSecurityKey(newPub) { KeyId = "prod-2026-05-25-rsa2048" },
    };
    // Default IssuerSigningKey resolution picks by `kid` header — order does not matter.
});
```

Switch issuance to the new key by updating `GameKit__Auth__Jwt__SigningKeyPath`
and `GameKit__Auth__Jwt__Kid` to the new pair. New tokens carry `kid=prod-2026-05-25-rsa2048`
in their header; the validator sees both keys and resolves the right one per token.

Rolling-restart the app fleet so every replica picks up the new config.

### Step 3 — wait out the refresh-token TTL

For 30 days (the default refresh-token lifetime), the validator must accept BOTH
keys, because:

- Players who logged in BEFORE the rotation hold access tokens signed with the
  old key (15 min remaining).
- Their refresh tokens are valid for 30 days; the next refresh issues a NEW
  access token signed with the new key.
- After 30 days every player has refreshed at least once (or has been logged
  out by inactivity), and no live access token references the old key.

Mark this date on the deploy calendar.

### Step 4 — retire the old key

After the grace period:

```csharp
// Drop the old key from IssuerSigningKeys.
jwtOpts.TokenValidationParameters.IssuerSigningKeys = new[]
{
    new RsaSecurityKey(LoadRsaPublicKey("/srv/mygame/keys/prod-2026-05-25-rsa2048-pub.pem"))
        { KeyId = "prod-2026-05-25-rsa2048" },
};
```

Rolling-restart again. Then archive the old key (cold storage, encrypted) for
the legally-required audit window, and securely shred the live copy:

```bash
shred -u /srv/mygame/keys/prod-2026-04-25-rsa2048-priv.pem
rm /srv/mygame/keys/prod-2026-04-25-rsa2048-pub.pem
```

### Emergency rotation (key compromise)

If you suspect the private key has been exfiltrated, the safe path is:

1. **Immediately rotate** — Steps 1+2 as above.
2. **Force-logout every player** — revoke every refresh-token family. The
   sliding-window 30-day acceptance is the wrong move when the key is known
   compromised. The `gamekit` CLI does not yet ship a "revoke-all" command; the
   manual SQL is:

   ```sql
   UPDATE gamekit.refresh_tokens
   SET revoked_at = NOW(), revoked_reason = 'security:key-rotation-emergency'
   WHERE revoked_at IS NULL;
   ```

3. **Retire the old key immediately** — skip Step 3's grace period; remove from
   `IssuerSigningKeys` and restart.
4. **Post-incident review** — log every access during the suspected compromise
   window via the `admin_audit_log` table; cross-reference player actions.

---

## Operational checks

```bash
# 1. Confirm the keys are present and readable by the app user.
sudo -u mygame ls -l /srv/mygame/keys/
# Expect each *-priv.pem to be -rw------- mygame mygame

# 2. Confirm the issued JWT carries the expected kid.
TOKEN=$(curl -sf -X POST https://mygame.example.com/auth/login/guest | jq -r '.access_token')
echo "$TOKEN" | cut -d. -f1 | base64 -d 2>/dev/null | jq .kid
# Expect: "prod-2026-05-25-rsa2048"  (or whichever kid is currently active)

# 3. Confirm the validator accepts BOTH keys during the grace window.
#    (Use a token minted before rotation; should still authenticate against /auth/me.)
curl -sf -H "Authorization: Bearer $OLD_TOKEN" https://mygame.example.com/auth/me

# 4. Confirm the old key is rejected after retirement (Step 4 + grace window expired).
#    (A token signed by the retired key should return 401.)
```

---

## Common mistakes to avoid

- **Sharing one key across deployments.** Each environment (dev / staging /
  production) must have its own keypair. A staging key leak should not invalidate
  production sessions.
- **Storing the private key in `appsettings.json` as an inline string.** It will
  land in git, on backup volumes, in container layers, and in `ps`-style
  process-list dumps. Always use file-path references + restricted file modes.
- **Logging the key bytes at startup.** Some teams add `_logger.LogInformation("Loaded key: {Key}", privateKey.ToString())` for debugging. Never do this — every log
  aggregator (Loki, Splunk, ELK) now holds the key.
- **Skipping the grace period.** Rotating without keeping the old key in
  `IssuerSigningKeys` for the refresh-token lifetime nukes every active session.
- **Rotating without changing the `Kid`.** Without a new `kid`, the validator
  cannot tell old tokens from new tokens; it tries both keys per request, and
  if the wrong one resolves first you get sporadic 401s.
- **Forgetting NTP.** A clock-drifted host issues JWTs with `nbf` (not-before)
  in the future or `exp` (expiry) in the past — the validator rejects every
  one. See [`bare-metal.md`](bare-metal.md) for `chrony` setup.

---

## Related runbooks

- [`bare-metal.md`](bare-metal.md) — `chrony` NTP setup; file-mode discipline.
- [`disaster-recovery.md`](disaster-recovery.md) — key restoration during a
  restore.
- [`postgres-roles.md`](postgres-roles.md) — `gamekit.refresh_tokens` lives in
  the schema the role grants protect.
- `samples/TicTacToeDuel/keys/README.md` — the dev key bootstrap this doc
  extends.
- `scripts/gen-test-rsa-pem.sh` — the dev key generation script (read it; it is
  the same `openssl genpkey` + `openssl rsa` + `chmod` sequence the production
  procedure above expands).
