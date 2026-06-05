---
phase: 07-core-rating-seam-stateless-auth-packages
plan: "02"
subsystem: auth
tags: [auth, argon2, password, hashing, rehash, cpm, sln]
dependency_graph:
  requires: [07-01]
  provides: [IPasswordHasher.NeedsRehash, GameKit.Auth.Argon2, Phase7-CPM-pins, Phase7-sln-entries]
  affects: [07-03, 07-04, 07-05, 07-06]
tech_stack:
  added:
    - Isopoh.Cryptography.Argon2 2.0.0 (CC0, fully managed, 8yr/7M+ downloads)
    - Isopoh.Cryptography.Blake2b 2.0.0 (transitive, pinned for CPM)
    - Isopoh.Cryptography.SecureArray 2.0.0 (transitive, pinned for CPM)
    - Microsoft.AspNetCore.Authentication.Google 10.0.8 (sln/CPM only — implementation in 07-03)
    - AspNet.Security.OAuth.Apple 10.0.0 (sln/CPM only — implementation in 07-04)
    - Microsoft.IdentityModel.Protocols.OpenIdConnect 8.14.0 (Apple transitive)
  patterns:
    - Sibling auth package shape (GameKit.Auth.Argon2 mirrors GameKit.Auth structure)
    - IPasswordHasher.NeedsRehash rehash-on-verify seam (AUTH-18 prerequisite)
    - UseArgon2() builder extension replaces BCryptPasswordHasher singleton
    - Dual-verify dispatch on $2a$/$2b$ prefix for live BCrypt→Argon2 migration
key_files:
  created:
    - src/GameKit.Auth.Argon2/GameKit.Auth.Argon2.csproj
    - src/GameKit.Auth.Argon2/AssemblyInfo.cs
    - src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs
    - src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs
    - src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs
    - tests/GameKit.Auth.Argon2.Tests/GameKit.Auth.Argon2.Tests.csproj
    - tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs
  modified:
    - src/GameKit.Auth/Services/IPasswordHasher.cs (NeedsRehash method added)
    - src/GameKit.Auth/Services/BCryptPasswordHasher.cs (NeedsRehash => false)
    - Directory.Packages.props (6 new CPM pins + IdentityModel bump)
    - GameKit.sln (8 new project entries)
decisions:
  - "Isopoh static Argon2.Hash(string, ..., SecureArray.DefaultCall) used for Hash() — Hash(Argon2Config) without explicit Salt produces truncated encoded string; static string overload handles random salt internally"
  - "Microsoft.IdentityModel.Tokens + System.IdentityModel.Tokens.Jwt bumped 8.3.0 → 8.14.0 to resolve NU1109 diamond conflict from OpenIdConnect 8.14.0"
  - "Argon2.Verify(hash, password) confirmed: encoded hash IS the first argument (Wave 0 round-trip resolves RESEARCH open question A3)"
metrics:
  duration: "~12min"
  completed: "2026-06-05"
  tasks: 4
  files: 12
---

# Phase 07 Plan 02: IPasswordHasher.NeedsRehash + GameKit.Auth.Argon2 + Shared CPM/sln Infra Summary

Argon2id opt-in sibling package with dual-verify BCrypt live-migration, NeedsRehash seam on the interface, and full Phase 7 shared CPM/sln infrastructure.

## What Was Built

### Task 1: Shared CPM + sln Infra
Added 6 CPM pins (`Isopoh.Cryptography.Argon2/Blake2b/SecureArray` 2.0.0, `Microsoft.AspNetCore.Authentication.Google` 10.0.8, `AspNet.Security.OAuth.Apple` 10.0.0, `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.14.0) and registered 8 new project entries in `GameKit.sln` (4 library + 4 test csproj paths) so Plans 03/04/05/06 can run in parallel without sln write conflicts.

### Task 2: IPasswordHasher.NeedsRehash
Added `bool NeedsRehash(string hash)` to the `IPasswordHasher` interface with full XML documentation. `BCryptPasswordHasher.NeedsRehash` always returns `false`. `GameKit.Auth` builds with 0 errors/warnings.

### Task 3: GameKit.Auth.Argon2 Package Scaffold
Created the complete package structure: `GameKit.Auth.Argon2.csproj` (no EF Design, no migrations; BCrypt.Net-Next for live migration window), `AssemblyInfo.cs` (InternalsVisibleTo grant), `GameKitArgon2Options.cs` (m=65536/t=3/p=1 OWASP defaults), `Argon2BuilderExtensions.UseArgon2()` (RemoveAll<IPasswordHasher> + AddSingleton<Argon2idPasswordHasher>), and `Argon2idPasswordHasher.cs`.

### Task 4: Argon2HasherTests — Wave 0 Round-Trip
Created test project with 12 unit tests covering all behavior bullets. All pass. Critically the round-trip test (`Hash → Verify`) proves `Argon2.Verify(hash, password)` argument order (encoded hash is FIRST — resolves RESEARCH open question A3). BCrypt-compat test validates live migration path. OWASP floor guard asserts defaults.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] NU1109 diamond-dependency: IdentityModel pins needed upgrading**
- **Found during:** Task 3 first build
- **Issue:** `Microsoft.IdentityModel.Protocols.OpenIdConnect 8.14.0` (new CPM pin for Apple) pulls `System.IdentityModel.Tokens.Jwt >= 8.14.0` and `Microsoft.IdentityModel.Tokens >= 8.14.0`, but both were pinned at `8.3.0` in Directory.Packages.props, triggering NU1109.
- **Fix:** Bumped `Microsoft.IdentityModel.Tokens` and `System.IdentityModel.Tokens.Jwt` from `8.3.0` to `8.14.0`. Both packages are binary-compatible — JwtBearer 10.0.6 uses IdentityModel 8.x internally; the bump is within the same major version. Verified by building `GameKit.Auth` after the bump (0 errors, 0 warnings).
- **Files modified:** `Directory.Packages.props`
- **Commit:** f60037d

**2. [Rule 1 - Bug] Isopoh Hash(Argon2Config) produces truncated encoded string without explicit Salt**
- **Found during:** Task 4 tests (Hash_Returns_Argon2id_Prefix FAIL — returned `"$argon2id$v=19$m=1024,t=1,p=1"` with no salt or hash body)
- **Issue:** `Argon2.Hash(cfg)` with `cfg.Salt = null` produces a truncated encoded string (params only, no actual hash). RESEARCH described the instance `argon2.Hash().ToString()` path (which returns the type name). Neither approach worked.
- **Fix:** Used the static `Argon2.Hash(string password, int timeCost, int memoryCost, int parallelism, Argon2Type type, int hashLength, SecureArrayCall)` overload which handles random salt generation internally and returns a complete `$argon2id$...` encoded string.
- **Files modified:** `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs`
- **Commit:** c552427

## Open Question Resolutions

**A3 (RESEARCH): Isopoh Argon2.Verify argument order**
Confirmed: `Argon2.Verify(hash, password)` — encoded hash is the FIRST argument. Proven by the round-trip test in Argon2HasherTests.

## Threat Coverage

| Threat | Status |
|--------|--------|
| T-07-02-01 (weak Argon2 params) | MITIGATED — OWASP floor guard asserts m≥19456/t≥2 at test time |
| T-07-02-02 (hash-format confusion) | MITIGATED — prefix dispatch tested; malformed → false (no throw) |
| T-07-02-03 (timing oracle) | ACCEPTED — documented in plan; no new oracle introduced |
| T-07-02-04 (DoS memory) | ACCEPTED — Lanes=1/Threads=1 caps at 64 MiB; documented |
| T-07-02-SC (supply chain) | MITIGATED — Isopoh packages approved in Package Legitimacy Audit |

## Known Stubs

None. All public API members are fully implemented and tested.

## Self-Check: PASSED

- `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs` exists: FOUND
- `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs` exists: FOUND
- `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs` exists: FOUND
- `src/GameKit.Auth/Services/IPasswordHasher.cs` contains NeedsRehash: FOUND
- Commit a71cdf8 (Task 1): FOUND
- Commit 41730da (Task 2): FOUND
- Commit f60037d (Task 3): FOUND
- Commit c552427 (Task 4): FOUND
