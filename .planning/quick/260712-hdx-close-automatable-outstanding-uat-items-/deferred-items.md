# Deferred / Blocking-Issue Notes — 260712-hdx

## RESOLVED: NU1903 Microsoft.OpenApi 2.0.0 HIGH severity vulnerability (GHSA-v5pm-xwqc-g5wc)

- **Found during:** Task 1, full-solution `dotnet build GameKit.sln -c Release` sanity check
  after the Scriban.Signed pin. Initially logged here as out-of-scope/deferred.
- **Escalated to fixed:** Task 2 requires building and running `samples/TicTacToeDuel` to
  drive the live browser UAT — that project directly hits this same NU1903 gate, making it a
  genuine Rule 3 blocking issue for the plan's required deliverable (not merely an unrelated
  discovery). Fixed by pinning `Microsoft.OpenApi` to `2.10.0` in `Directory.Packages.props`
  (transitive-only pin; `Microsoft.AspNetCore.OpenApi` 10.0.8 requires 2.0.0, which falls in
  the vulnerable range `>= 2.0.0-preview.11, <= 2.7.4`; 2.7.5 is first-patched, 2.10.0 is
  latest-stable on the same 2.x line — deliberately not the 3.x line, which is a
  source-breaking OpenAPI-3.1 rewrite). Mirrors the MessagePack 3.1.7 / Scriban.Signed 7.2.5
  transitive-pin precedents already in the file.
- **Verified:** `dotnet build samples/TicTacToeDuel -c Release` now succeeds (previously
  failed with the same NU1903 error affecting `src/GameKit.OpenApi`,
  `tests/GameKit.OpenApi.Integration.Tests`, `tests/GameKit.Platformer3D.Integration.Tests`,
  `tests/GameKit.Platformer3D.Tests`, `tests/GameKit.Distribution.Integration.Tests`,
  `samples/Platformer3D`, and `samples/TicTacToeDuel`).
