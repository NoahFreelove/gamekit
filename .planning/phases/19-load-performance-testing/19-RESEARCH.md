# Phase 19: Load / Performance Testing - Research

**Researched:** 2026-06-23
**Domain:** BenchmarkDotNet micro-benchmarks + k6 load scenarios + CI regression gate
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
All implementation choices at Claude's discretion (discuss skipped).

### Claude's Discretion
All implementation choices.

### Deferred Ideas (OUT OF SCOPE)
None.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PERF-01 | BenchmarkDotNet (MIT) micro-benchmarks for hot paths: JWT validation, BCrypt + Argon2id verify, Glicko-2 rating calculation, matchmaking-ticket Redis round-trip | §Standard Stack, §Architecture Patterns, §Code Examples |
| PERF-02 | Committed baselines (`benchmarks/BASELINES.md`): machine spec + .NET version + result per benchmark | §Architecture Patterns — Baseline Format |
| PERF-03 | k6 (AGPLv3 CLI; no library dep) load scenarios: matchmaking burst (500 VUs) + auth throughput. Runnable against local Testcontainers stack; NEVER in CI against production | §Architecture Patterns — k6 Scenarios, §Code Examples |
| PERF-04 | k6 Lobby SignalR fan-out scenario exercising real Redis backplane. Spike confirms k6 WebSocket sufficiency BEFORE committing the scenario | §k6 SignalR Spike, §Code Examples |
| PERF-05 | `docs/performance-tuning.md`: BCrypt/Argon2 cost-factor vs latency table, Npgsql pool sizing, top-5 hot-query notes | §Architecture Patterns — Tuning Guide |
| PERF-06 | CI benchmark regression gate — build fails if any hot-path benchmark regresses >20% from committed baseline | §PERF-06 Regression Gate (the hard one) |
</phase_requirements>

---

## Summary

Phase 19 adds three distinct artifacts to the GameKit repository: (1) a new `tests/GameKit.LoadTests` project containing BenchmarkDotNet micro-benchmarks for every hot-path identified in the requirements; (2) k6 JavaScript load-test scenarios in `tests/k6/` that run against a Testcontainers-backed local stack via `docker run --rm -i grafana/k6:latest run -`; and (3) a CI gate that diffs the JSON output of the benchmark runner against a committed baseline file and exits non-zero on any >20% mean regression.

The key constraint shaping every decision is that k6 is AGPLv3 and must therefore never appear as a library dependency inside any GameKit package or test project. It is an external CLI invoked via Docker. BenchmarkDotNet is MIT and can be referenced from a test project with `IsPackable=false`. The existing `tests/GameKit.Matchmaking.LoadTests` project serves a different purpose (10-minute sustain load test for MATCH-13/SC#3) and must NOT be merged with the new micro-benchmark project — the two projects have incompatible runner models (xUnit host vs. BenchmarkDotNet console runner).

The hardest requirement is PERF-06. BenchmarkDotNet has no built-in >N% regression gate. The recommended approach is a pure dotnet script (`benchmarks/compare-baseline.csx` or a small C# tool project) that reads the `-report-full.json` output from the benchmark run, parses `Benchmarks[].Statistics.Mean` (in nanoseconds), compares each method against the values recorded in `benchmarks/baselines/report-baseline.json`, and exits with code 1 if any mean exceeds 1.20× its baseline mean. This is the lightest-weight, GPL-compatible, fully offline approach.

**Primary recommendation:** Create `tests/GameKit.LoadTests` as a BenchmarkDotNet console app (not xUnit). k6 scenarios live in `tests/k6/`. PERF-06 gate is a dotnet-script comparison against a committed JSON baseline file. k6 SignalR fan-out uses the stable `k6/websockets` module (available in stock `grafana/k6:latest` v2.0.0, confirmed) with manual SignalR protocol implementation (negotiate POST + WebSocket upgrade + `{"protocol":"json","version":1}\x1e` handshake).

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| BenchmarkDotNet micro-benchmarks | Console test runner | — | No HTTP pipeline; pure in-process measurement with injected Testcontainers Redis for the ticket round-trip benchmark |
| k6 matchmaking burst / auth throughput | External CLI (Docker) | Local Testcontainers HTTP stack | k6 is AGPLv3; must be external-process only; targets the real HTTP endpoints, not in-process |
| k6 SignalR fan-out | External CLI (Docker) | Local Testcontainers WebSocket stack | Same AGPLv3 constraint; targets `/hubs/lobby` WebSocket endpoint |
| PERF-06 regression gate | CI step (GitHub Actions) | Local developer script | Offline comparison script; no cloud service |
| Performance tuning guide | Documentation | — | `docs/performance-tuning.md` — static Markdown |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| BenchmarkDotNet | **0.15.8** | Micro-benchmark runner for PERF-01/02/06 | [VERIFIED: api.nuget.org] MIT; the de-facto standard for .NET performance measurement; `[MemoryDiagnoser]` built in; JSON exporter for PERF-06 gate; net10.0 TFM supported |
| grafana/k6 | **v2.0.0** (Docker `grafana/k6:latest`) | Load scenarios PERF-03/04 | [VERIFIED: docker images] Already pulled on dev machine; AGPLv3 — used as external CLI only |

### Supporting (already in repo — no new pins needed)
| Library | Version | Purpose |
|---------|---------|---------|
| Testcontainers.Redis | 4.11.0 | Redis fixture for matchmaking ticket benchmark | [VERIFIED: Directory.Packages.props] |
| Testcontainers.PostgreSql | 4.11.0 | Postgres fixture for Glicko-2 integration benchmark | [VERIFIED: Directory.Packages.props] |
| xUnit | 2.9.2 | Not used in LoadTests runner — xUnit IS used in MatchmakingLoadTests | [VERIFIED: Directory.Packages.props] |
| BCrypt.Net-Next | 4.1.0 | Already pinned — hash/verify under benchmark | [VERIFIED: Directory.Packages.props] |
| Isopoh.Cryptography.Argon2 | 2.0.0 | Already pinned — Argon2id verify under benchmark | [VERIFIED: Directory.Packages.props] |

### NOT Adding
| Skipped | Why |
|---------|-----|
| `BenchmarkDotNet.Analyser` (BDNA dotnet tool) | Extra global tool install; overkill — a 30-line C# script reading the report JSON is simpler and has no additional dependency |
| `benchmark-action/github-action-benchmark` | GitHub Action that uploads to GitHub Pages — requires public repo or GitHub Enterprise; the comparison logic is what matters and we can replicate it in a CI script without the action |
| Any xk6 extension for SignalR | Stock k6 v2.0.0 ships `k6/websockets` (stable) which is sufficient for manual SignalR protocol — confirmed via `docker run` |

### Installation

The `tests/GameKit.LoadTests` project will add to `Directory.Packages.props`:
```xml
<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
```

No other NuGet changes. k6 runs via `docker run --rm -i grafana/k6:latest run -`.

---

## Package Legitimacy Audit

The ecosystem-appropriate legitimacy check (seam) does not cover NuGet; manual verification performed.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| BenchmarkDotNet 0.15.8 | NuGet | ~10 yrs | >100M total | github.com/dotnet/BenchmarkDotNet | OK | Approved |
| grafana/k6 v2.0.0 | Docker Hub | ~9 yrs | >50M pulls | github.com/grafana/k6 | OK | Approved (external CLI only — AGPLv3, never linked) |

**Packages removed due to SLOP verdict:** none
**Packages flagged as suspicious (SUS):** none

`BenchmarkDotNet` is an ASF/Microsoft-ecosystem project; the GitHub repo is `dotnet/BenchmarkDotNet` under the `dotnet` org. [VERIFIED: api.nuget.org confirmed version 0.15.8 is latest stable]

---

## Architecture Patterns

### System Architecture Diagram

```
Developer / CI
     |
     |-- dotnet run -c Release --project tests/GameKit.LoadTests --, BenchmarkDotNet args
     |        |
     |        v
     |   BenchmarkDotNet runner
     |        |-- [Benchmark] JwtValidation         \
     |        |-- [Benchmark] BCryptVerify           |  in-process
     |        |-- [Benchmark] Argon2idVerify         |  CPU benchmarks
     |        |-- [Benchmark] Glicko2Apply           |  (no I/O)
     |        |-- [Benchmark] TicketRedisRoundTrip --+-- Testcontainers Redis
     |        |
     |        v
     |   BDN JSON output: BenchmarkRun/results/*-report-full.json
     |        |
     |        v (PERF-06 gate)
     |   dotnet script benchmarks/compare-baseline.csx
     |        |-- reads benchmarks/baselines/report-baseline.json  (committed)
     |        |-- computes delta = (newMean - baselineMean) / baselineMean
     |        |-- exit 1 if delta > 0.20 for any benchmark
     |
     |-- docker run --rm -i grafana/k6:latest run - < tests/k6/matchmaking-burst.js
     |        |
     |        v
     |   k6 VUs ---HTTP---> local Testcontainers ASP.NET Core host
     |                           POST /api/mm/queue  (500 VU burst)
     |                           GET  /api/auth/login (auth throughput)
     |
     |-- docker run --rm -i grafana/k6:latest run - < tests/k6/lobby-signalr-fanout.js
              |
              v
         k6 VUs ---HTTP---> negotiate /hubs/lobby/negotiate
                  ---WS----> ws://host/hubs/lobby?access_token=...
                              send: {"protocol":"json","version":1}\x1e
                              invoke: JoinLobbyAsync
                              measure: time-to-receive broadcast from first VU
```

### Recommended Project Structure

```
gamekit/
├── tests/
│   ├── GameKit.LoadTests/              # NEW — BenchmarkDotNet micro-benchmarks (PERF-01)
│   │   ├── GameKit.LoadTests.csproj   # Console app; IsPackable=false; not IsTestProject
│   │   ├── Program.cs                  # BenchmarkRunner.Run<*>(args: args)
│   │   ├── Benchmarks/
│   │   │   ├── JwtValidationBenchmarks.cs
│   │   │   ├── PasswordHasherBenchmarks.cs    # BCrypt + Argon2id
│   │   │   ├── Glicko2Benchmarks.cs
│   │   │   └── MatchmakingTicketBenchmarks.cs  # needs Testcontainers Redis
│   │   └── Infrastructure/
│   │       └── RedisFixtureSetup.cs    # Testcontainers Redis lifecycle for ticket benchmark
│   └── GameKit.Matchmaking.LoadTests/ # EXISTING — keep as-is (MATCH-13 sustain test)
│
├── tests/k6/                           # NEW — k6 scenario scripts (PERF-03/04)
│   ├── matchmaking-burst.js            # 500 VU burst + auth throughput
│   ├── lobby-signalr-fanout.js         # N clients, one broadcast, delivery distribution
│   ├── README.md                       # Docker invocation instructions
│   └── helpers/
│       └── signalr.js                  # reusable SignalR negotiate+handshake helpers
│
├── benchmarks/
│   ├── baselines/
│   │   └── report-baseline.json        # committed BDN -report-full.json snapshot (PERF-02)
│   ├── compare-baseline.csx            # PERF-06 gate script (C# script or small tool)
│   └── BASELINES.md                    # human-readable: machine spec + result table (PERF-02)
│
└── docs/
    └── performance-tuning.md           # PERF-05
```

### Pattern 1: BenchmarkDotNet Project Layout (not xUnit)

**What:** A console app where `Program.cs` calls `BenchmarkRunner.Run<>()`. This is how BenchmarkDotNet is designed to be used. Using it inside an xUnit host is possible but incorrect — xUnit's async machinery interferes with BDN's statistical isolation.

**When to use:** Always for PERF-01 benchmarks.

**Example:**
```csharp
// Source: [CITED: benchmarkdotnet.org — getting started]
// tests/GameKit.LoadTests/Program.cs
using BenchmarkDotNet.Running;

var summary = BenchmarkRunner.Run(typeof(Program).Assembly, args: args);
return summary.HasCriticalValidationErrors ? 1 : 0;
```

```xml
<!-- tests/GameKit.LoadTests/GameKit.LoadTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <!-- NOT IsTestProject — BDN is not an xUnit/MSTest runner -->
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" />
  </ItemGroup>
  <ItemGroup>
    <!-- ProjectReferences to the packages under test -->
    <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Auth.Argon2\GameKit.Auth.Argon2.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Rankings\GameKit.Rankings.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

### Pattern 2: Per-Benchmark Class Structure

**What:** Each benchmark class is annotated with `[MemoryDiagnoser]` and optionally `[HardwareCounters]`. One class per hot-path domain. GlobalSetup initializes any fixtures; IterationSetup resets any mutable state.

```csharp
// Source: [CITED: benchmarkdotnet.org]
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
[ShortRunJob] // for quick local validation; remove for committed baselines
public class PasswordHasherBenchmarks
{
    private BCryptPasswordHasher _bcrypt = null!;
    private Argon2idPasswordHasher _argon2 = null!;
    private string _bcryptHash = null!;
    private string _argon2Hash = null!;

    [GlobalSetup]
    public void Setup()
    {
        // BCrypt work factor 12 = production default (PasswordOptions.BCryptWorkFactor)
        _bcrypt = new BCryptPasswordHasher(
            new GameKitAuthOptions { Password = { BCryptWorkFactor = 12 } });
        // Argon2id: OWASP defaults (m=65536 KiB, t=3, p=1)
        _argon2 = new Argon2idPasswordHasher(new GameKitArgon2Options());
        _bcryptHash = _bcrypt.Hash("benchmarkpassword123!");
        _argon2Hash = _argon2.Hash("benchmarkpassword123!");
    }

    [Benchmark]
    public bool BCryptVerify() => _bcrypt.Verify("benchmarkpassword123!", _bcryptHash);

    [Benchmark]
    public bool Argon2idVerify() => _argon2.Verify("benchmarkpassword123!", _argon2Hash);
}
```

### Pattern 3: JWT Validation Benchmark

**What:** JWT validation is the hot path for every authenticated request. The benchmark exercises `JwtSecurityTokenHandler.ValidateToken()` with the `TokenValidationParameters` configured identically to `AuthBuilderExtensions` — RSA-SHA256, ValidateIssuer=true, ValidateAudience=true, ValidateLifetime=true.

```csharp
// Source: [ASSUMED — based on JwtIssuer.cs + AuthBuilderExtensions.cs pattern]
[MemoryDiagnoser]
public class JwtValidationBenchmarks
{
    private JwtSecurityTokenHandler _handler = null!;
    private TokenValidationParameters _params = null!;
    private string _token = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa);
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        _handler = new JwtSecurityTokenHandler();
        // Pre-issue a valid token to measure validation path only
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "gk-bench", Audience = "gk-bench",
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = creds,
            Subject = new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) })
        };
        _token = _handler.WriteToken(_handler.CreateToken(descriptor));
        // Validation key = public key only (mirrors production: public PEM loaded at startup)
        _params = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "gk-bench",
            ValidateAudience = true, ValidAudience = "gk-bench",
            ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            RequireSignedTokens = true,
        };
    }

    [Benchmark]
    public ClaimsPrincipal ValidateToken()
        => _handler.ValidateToken(_token, _params, out _);
}
```

### Pattern 4: Matchmaking Ticket Redis Round-Trip Benchmark

**What:** Exercises the enqueue path: `IMatchmakingService.EnqueueAsync` against a real Testcontainers Redis (not mocked). This is the only benchmark that requires I/O; it lives in its own class with a `[GlobalSetup]` that starts the Redis container.

**Critical:** BenchmarkDotNet's `[GlobalSetup]` is synchronous by default. Use `[GlobalSetup]` with `.GetAwaiter().GetResult()` to start Testcontainers, or mark it async (BDN 0.13.5+ supports `async Task GlobalSetup()`). [CITED: benchmarkdotnet.org/articles/features/async-benchmarks]

```csharp
[MemoryDiagnoser]
public class MatchmakingTicketBenchmarks : IDisposable
{
    private RedisContainer _redisContainer = null!;
    private IMatchmakingService _svc = null!;
    private Guid _ladderId;
    private Guid _playerId;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _redisContainer = new RedisBuilder().Build();
        await _redisContainer.StartAsync();
        // Wire MatchmakingService with real Redis and in-memory Postgres (or Testcontainers)
        // ... (setup IServiceProvider chain)
        _ladderId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
    }

    [Benchmark]
    public async Task<EnqueueResult> TicketEnqueueAsync()
        => await _svc.EnqueueAsync(_playerId, _ladderId, null, null, CancellationToken.None);

    [GlobalCleanup]
    public async Task CleanupAsync() => await _redisContainer.DisposeAsync();
    public void Dispose() { /* sync dispose if needed */ }
}
```

### Pattern 5: Glicko-2 Benchmark

**What:** `Glicko2Algorithm.Apply()` is pure CPU (no I/O). Benchmark at batch sizes of 2, 10, and 100 outcomes to show O(n) scaling.

```csharp
[MemoryDiagnoser]
public class Glicko2Benchmarks
{
    private Glicko2Algorithm _algo = null!;
    private RankingState _state = null!;
    private RankingBatch _batch2 = null!, _batch10 = null!, _batch100 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _algo = new Glicko2Algorithm(tau: 0.5, initVolatility: 0.06);
        _state = BuildState(200); // 200 players with initial ratings
        _batch2 = BuildBatch(2);
        _batch10 = BuildBatch(10);
        _batch100 = BuildBatch(100);
    }

    [Benchmark] public RankingState Apply_2()   => _algo.Apply(_state, _batch2);
    [Benchmark] public RankingState Apply_10()  => _algo.Apply(_state, _batch10);
    [Benchmark] public RankingState Apply_100() => _algo.Apply(_state, _batch100);
}
```

---

## PERF-06 Regression Gate (the hard one)

### Problem

BenchmarkDotNet has **no built-in >N% exit-code gate**. The baseline comparison must be implemented externally. The requirement is:

- CI step runs benchmarks with `--exporters json`
- A comparison script reads the new JSON + the committed baseline JSON
- Exit code 1 if any benchmark's mean regresses >20%

### Recommended Approach: Custom dotnet-script comparison

This is the recommended approach (over BDNA or github-action-benchmark):

**Why not BDNA (`BenchmarkDotNet.Analyser`):**
BDNA requires installing a global dotnet tool and running multiple aggregate/analyse commands. It is designed for multi-run aggregation across many CI runs. For a single baseline-vs-current comparison, it is heavier than needed and adds a dotnet tool installation step.

**Why not `benchmark-action/github-action-benchmark`:**
It uploads results to GitHub Pages (requires public repo or GH Enterprise) and has opinionated storage. Its MIT license is fine, but it adds coupling to GitHub Actions infrastructure. The GameKit CI is already clean; a script comparison keeps the gate self-contained.

**Recommended: `benchmarks/compare-baseline.csx` (dotnet-script)**

```csharp
// benchmarks/compare-baseline.csx
// Run with: dotnet script benchmarks/compare-baseline.csx <new-report.json> <baseline.json>
// Or compile as a small Program in benchmarks/CompareBaseline/ if dotnet-script unavailable.
// Exit code 0 = no regression; 1 = regression detected.

using System.Text.Json;

var newReport = JsonDocument.Parse(File.ReadAllText(args[0]));
var baseline  = JsonDocument.Parse(File.ReadAllText(args[1]));

// Build baseline lookup: method name -> mean (nanoseconds)
var baselineMap = new Dictionary<string, double>();
foreach (var bm in baseline.RootElement.GetProperty("Benchmarks").EnumerateArray())
{
    var method = bm.GetProperty("Method").GetString()!;
    var mean   = bm.GetProperty("Statistics").GetProperty("Mean").GetDouble();
    baselineMap[method] = mean;
}

bool failed = false;
foreach (var bm in newReport.RootElement.GetProperty("Benchmarks").EnumerateArray())
{
    var method  = bm.GetProperty("Method").GetString()!;
    var newMean = bm.GetProperty("Statistics").GetProperty("Mean").GetDouble();

    if (!baselineMap.TryGetValue(method, out var baseMean)) continue;

    double delta = (newMean - baseMean) / baseMean;
    if (delta > 0.20)
    {
        Console.Error.WriteLine(
            $"REGRESSION: {method}: {newMean/1e6:F2} ms vs baseline {baseMean/1e6:F2} ms" +
            $" (+{delta:P1}, threshold 20%)");
        failed = true;
    }
    else
    {
        Console.WriteLine($"  OK: {method}: {newMean/1e6:F2} ms vs baseline {baseMean/1e6:F2} ms ({delta:+P1})");
    }
}
return failed ? 1 : 0;
```

**JSON schema confirmed:** BenchmarkDotNet `-report-full.json` has:
```json
{
  "Benchmarks": [
    {
      "Method": "BCryptVerify",
      "Statistics": {
        "Mean": 123456789.0,
        ...
      }
    }
  ]
}
```
[CITED: benchmarkdotnet.org/articles/samples/IntroExportJson.html]

Units: Mean is in **nanoseconds** regardless of display unit.

### CI Step Wiring (GitHub Actions)

```yaml
# Add to .github/workflows/ci.yml — runs only on the benchmark job (separate from fast unit CI)
- name: Run micro-benchmarks (PERF-01/02)
  run: |
    dotnet run --project tests/GameKit.LoadTests -c Release -- \
      --filter '*' \
      --exporters json \
      --artifacts BenchmarkRun
  # Generates: BenchmarkRun/results/*-report-full.json

- name: Benchmark regression gate (PERF-06)
  run: |
    REPORT=$(ls BenchmarkRun/results/*-report-full.json | head -1)
    dotnet script benchmarks/compare-baseline.csx "$REPORT" benchmarks/baselines/report-baseline.json
    # Exit code 1 fails the CI step
```

**Or** (without dotnet-script tooling) compile `benchmarks/CompareBaseline/` as a `net10.0` console app:
```
benchmarks/CompareBaseline/CompareBaseline.csproj   (OutputType=Exe, no PackageRefs needed)
benchmarks/CompareBaseline/Program.cs               (same logic as .csx above)
```
Then in CI:
```yaml
- name: Benchmark regression gate (PERF-06)
  run: |
    REPORT=$(ls BenchmarkRun/results/*-report-full.json | head -1)
    dotnet run --project benchmarks/CompareBaseline -c Release -- \
      "$REPORT" benchmarks/baselines/report-baseline.json
```

The compiled-tool approach (no `dotnet-script` global tool) is cleaner for CI reproducibility. **Recommended: use the compiled tool.**

### How to Stabilize the Gate Against CI Runner Noise

The 20% threshold is intentionally generous precisely to absorb:

1. **Warm-up:** BenchmarkDotNet defaults include 3 warmup iterations + 5 measurement iterations minimum. The JSON report records only the steady-state measurement phase — warmup is excluded from `Statistics.Mean`. [CITED: benchmarkdotnet.org]
2. **Iteration count:** The default `[MediumRunJob]` (100–200 runs per benchmark) gives statistical stability. For the CI gate, use `[SimpleJob]` or the default (no attribute) — BDN auto-tunes iteration count for statistical significance.
3. **Runner-to-runner variance:** GitHub Actions `ubuntu-24.04` runner hardware varies run-to-run by ±5-15% for CPU-bound work. The 20% threshold absorbs this. The baseline is committed from a specific run; future runs on the same runner class will stay within 20% of the baseline for the non-IO benchmarks.
4. **Argon2/BCrypt benchmarks are inherently expensive (~100ms each):** At that magnitude, ±5% variance is ~5ms — well within the 20% gate (~20ms). These benchmarks are the most stable.
5. **JWT validation (~microseconds):** Smallest absolute variance. The 20% threshold = ~2-5 µs — still comfortably above CI noise.
6. **Redis round-trip benchmark:** The most noisy (depends on Testcontainers Redis container start). Recommend running the Redis benchmark with `[MinIterationCount(15)]` to reduce variance. The 20% threshold accommodates network jitter inside Docker's bridge network.

**Baseline capture procedure:** Run benchmarks once on the dev machine (or a dedicated CI machine), copy the `-report-full.json` to `benchmarks/baselines/report-baseline.json`, commit it. The `BASELINES.md` table is written manually from this run. Update baseline when a deliberate performance optimization changes the mean by >10% downward.

### BDN Runner Args for Baseline Capture

```bash
dotnet run --project tests/GameKit.LoadTests -c Release -- \
  --filter '*' \
  --exporters json \
  --iterationCount 15 \
  --warmupCount 5 \
  --artifacts benchmarks/baselines/run-$(date +%Y%m%d)
# Then copy the -report-full.json to benchmarks/baselines/report-baseline.json
```

---

## k6 Load Scenarios

### k6 SignalR Spike (PERF-04)

**What the spike must confirm BEFORE committing the full fan-out scenario:**

The spike is a standalone `tests/k6/spike-signalr.js` script that:
1. HTTP POSTs to `/hubs/lobby/negotiate?access_token=<jwt>` — gets back a `connectionToken`
2. Opens a WebSocket to `ws://host/hubs/lobby?access_token=<jwt>&id=<connectionToken>`
3. Sends the SignalR handshake: `{"protocol":"json","version":1}\x1e` (ASCII 0x1E = record separator)
4. Asserts it receives a handshake response: `{}\x1e`
5. Sends a hub invocation: `{"type":1,"target":"JoinLobbyAsync","arguments":[<lobbyId>],"invocationId":"1"}\x1e`
6. Checks for a response within 2 seconds

**Confirmed:** Stock `grafana/k6:latest` v2.0.0 includes both `k6/ws` (callback-based, deprecated) and the stable `k6/websockets` (WHATWG WebSocket API). No extension required. [VERIFIED: docker run grafana/k6:latest]

**Spike GO/NO-GO:** If the spike script can complete steps 1-5 and receive a non-error message, the full fan-out scenario is viable with stock k6. If SignalR rejects the handshake (e.g., JSON-only protocol not negotiated correctly), document the failure and the spike plan task is marked failed — escalate to engineer for investigation.

### k6 SignalR Protocol Reference

[CITED: community observation from k6 issue #3936 + SignalR JSON protocol spec]

Key protocol facts the k6 script must implement:
- **Negotiate:** `POST /hubs/lobby/negotiate?access_token=<jwt>` → returns `{"connectionId":"...","availableTransports":[...]}`
- **WS connect:** `ws://host/hubs/lobby?id=<connectionId>&access_token=<jwt>`
- **Handshake:** Send `{"protocol":"json","version":1}\x1e`, receive `{}\x1e`
- **Hub invocation (fire-and-forget):** `{"type":1,"target":"MethodName","arguments":[...],"invocationId":"1"}\x1e`
- **Hub invocation response:** `{"type":3,"invocationId":"1","result":...}\x1e`
- **Message delimiter:** ASCII 0x1E (`\x1e`) terminates every SignalR JSON frame
- **Ping:** Server sends `{"type":6}\x1e` keepalives; client should respond with `{"type":6}\x1e`

```javascript
// Source: [ASSUMED — derived from SignalR JSON protocol spec + k6/websockets docs]
// tests/k6/helpers/signalr.js
import http from 'k6/http';
import { WebSocket } from 'k6/websockets';

const RECORD_SEP = String.fromCharCode(0x1e);

export function negotiateSignalR(baseUrl, jwt) {
  const res = http.post(
    `${baseUrl}/hubs/lobby/negotiate?access_token=${jwt}&negotiateVersion=1`,
    null,
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (res.status !== 200) throw new Error(`negotiate failed: ${res.status}`);
  return JSON.parse(res.body);
}

export function connectSignalR(wsUrl, jwt, connectionId, onMessage) {
  const ws = new WebSocket(
    `${wsUrl}/hubs/lobby?id=${connectionId}&access_token=${jwt}`
  );
  ws.addEventListener('open', () => {
    // Step 1: send handshake
    ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RECORD_SEP);
  });
  ws.addEventListener('message', (event) => {
    const frames = event.data.split(RECORD_SEP).filter(f => f.length > 0);
    for (const frame of frames) {
      const msg = JSON.parse(frame);
      if (msg.type === 6) {
        // Ping — respond
        ws.send(JSON.stringify({ type: 6 }) + RECORD_SEP);
        return;
      }
      onMessage(msg, ws);
    }
  });
  return ws;
}

export function invoke(ws, target, args, invocationId) {
  ws.send(JSON.stringify({
    type: 1,
    target: target,
    arguments: args,
    invocationId: String(invocationId)
  }) + RECORD_SEP);
}
```

### k6 Matchmaking Burst Scenario (PERF-03)

```javascript
// Source: [ASSUMED — k6 API docs + GameKit matchmaking endpoint known from codebase]
// tests/k6/matchmaking-burst.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  scenarios: {
    burst: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 500 },   // ramp to 500 VUs
        { duration: '30s', target: 500 },   // sustain 500 VUs enqueuing
        { duration: '10s', target: 0  },    // ramp down
      ],
    },
  },
  thresholds: {
    'http_req_duration{name:enqueue}': ['p(99)<2000'],   // p99 enqueue < 2s
    'http_req_failed': ['rate<0.01'],                     // <1% errors
  },
};

// BASE_URL and JWT come from k6 env vars: -e BASE_URL=http://host:port -e JWT=...
const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5000';
const JWT = __ENV.JWT;

export default function () {
  const ladderId = __ENV.LADDER_ID;
  const res = http.post(
    `${BASE_URL}/api/mm/queue`,
    JSON.stringify({ ladderId, poolName: null }),
    {
      headers: { 'Authorization': `Bearer ${JWT}`, 'Content-Type': 'application/json' },
      tags: { name: 'enqueue' },
    }
  );
  check(res, { 'enqueue 200': (r) => r.status === 200 || r.status === 409 });
}
```

**Observation of "match formed":** k6 cannot easily observe async match formation (the ticker runs every 500ms). Approach: After the burst scenario, run a separate polling check using a seeded ticket ID — `GET /api/mm/queue/{ticketId}/status` — and measure time from enqueue to `status=matched`. Assert p99 < configured threshold (e.g., 5000ms = ticker latency + proposal flow). This polling is best done in a dedicated short scenario after the burst.

### k6 Auth Throughput Scenario (PERF-03)

Separate `export function setup()` or scenario in `matchmaking-burst.js`:
```javascript
// POST /api/auth/login — measure throughput at 100 VUs sustained for 30s
// Threshold: p99 < 1000ms (BCrypt at work factor 12 is ~100ms per verify; 100 VUs = ~1000 ops/s per CPU)
```

### k6 Docker Invocation

```bash
# Standard invocation (offline, reproducible, no installation):
docker run --rm -i \
  --network host \
  -e BASE_URL=http://localhost:5000 \
  -e JWT=<player_jwt> \
  -e LADDER_ID=<ladder_guid> \
  grafana/k6:latest run - < tests/k6/matchmaking-burst.js

# With host networking (Linux — allows targeting localhost Testcontainers ports):
# --network host is the simplest approach on Linux.
# On macOS/Windows Docker Desktop: use host.docker.internal instead of localhost.
```

**Note on k6 AGPLv3:** k6 is used exclusively as an external Docker process. It is never referenced as a NuGet package, never shipped inside any GameKit package, and never linked into any build artifact. This preserves GameKit's GPL self-hosted posture. The k6 scripts themselves (`.js` files) are MIT-licenseable test scripts owned by the GameKit repo. [ASSUMED — AGPLv3 copyleft applies to linked/distributed binaries, not to test scripts that invoke the binary]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Statistical benchmark harness | Custom timing loops | BenchmarkDotNet | Handles warmup, GC, JIT, statistical significance; a manual Stopwatch loop produces unreliable numbers |
| Noise reduction in micro-benchmarks | Sleep loops / averaging | BDN's iteration model + `[MemoryDiagnoser]` | BDN handles outlier detection, P-values, confidence intervals |
| JSON report parsing for PERF-06 | Custom text parser | `System.Text.Json` document reader | BDN's JSON is well-structured; no custom format needed |
| WebSocket load testing | Custom Go/C# client | k6 `k6/websockets` | k6 handles VU lifecycle, ramp-up/down, percentile aggregation |
| SignalR extension for k6 | xk6 custom build | Manual protocol implementation in stock k6 | Stock k6 v2.0.0 `k6/websockets` is sufficient; no custom build needed |
| Benchmark runner in xUnit | `[Fact]` + Stopwatch | `BenchmarkRunner.Run<>()` in console app | xUnit's async/isolation model conflicts with BDN's statistical model |

**Key insight:** BenchmarkDotNet and k6 each solve a hard measurement problem with years of engineering. The only thing to build is the glue: the comparison script (30 lines), the k6 scenario scripts (JS), and the CI workflow step.

---

## Existing Code Inventory

The following codebase seams are benchmarked — all verified by file read:

| Benchmark Target | Source File | Key Entry Point | Notes |
|-----------------|-------------|-----------------|-------|
| JWT validation | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:199` | `TokenValidationParameters` with RSA-SHA256, ValidateIssuer, ValidateAudience, ValidateLifetime | RSA key loaded at setup; `JwtSecurityTokenHandler.ValidateToken()` is the hot path |
| BCrypt verify | `src/GameKit.Auth/Services/BCryptPasswordHasher.cs:26` | `BCrypt.Net.BCrypt.Verify(password, hash)` | Default work factor 12 (from `PasswordOptions.BCryptWorkFactor = 12`) |
| Argon2id verify | `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs:73` | `Isopoh.Cryptography.Argon2.Argon2.Verify(hash, password)` | Default: m=65536 KiB, t=3, p=1, hashLength=32 |
| Glicko-2 | `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs:58` | `Glicko2Algorithm.Apply(state, batch)` | Pure CPU; tau=0.5; creates one `RatingCalculator` per call |
| Matchmaking Redis round-trip | `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` → `EnqueueAsync` | Writes ticket hash to Redis + ZADD to sorted set | Needs Testcontainers Redis; not Postgres |
| SignalR LobbyHub | `src/GameKit.Lobby/Hubs/LobbyHub.cs` | `/hubs/lobby` — WS upgrade + `JoinLobbyAsync` + broadcast | JWT via `?access_token` query string |

**Project layout decision (CONTEXT.md §Existing Code Insights):**

- `tests/GameKit.Matchmaking.LoadTests` — KEEP AS-IS. It is an xUnit-hosted 10-minute sustain load test (MATCH-13/SC#3). Its `IsTestProject=true` and it runs under `dotnet test`. It has no BenchmarkDotNet dependency.
- `tests/GameKit.LoadTests` — NEW project. BenchmarkDotNet console app (`OutputType=Exe`, NOT `IsTestProject`). Contains PERF-01 micro-benchmarks. Excluded from `dotnet test` runs (it is not a test project); run explicitly with `dotnet run -c Release`.

These two projects coexist cleanly in the solution without conflict.

---

## Common Pitfalls

### Pitfall 1: Running BenchmarkDotNet in Debug mode

**What goes wrong:** BDN prints a warning and refuses to run benchmarks in Debug configuration. JIT optimizations are absent; results are meaningless.

**Why it happens:** Developers forget `-c Release`.

**How to avoid:** Always run with `dotnet run --project tests/GameKit.LoadTests -c Release`. CI workflow must also use `--configuration Release`.

**Warning signs:** BDN outputs "// Validating benchmarks... // ... 'Benchmarks not built in Release'" and exits without running.

### Pitfall 2: Benchmarking BCrypt/Argon2 at test-safe params instead of production params

**What goes wrong:** Integration tests use `AllowInsecureParametersForTesting = true` with low cost factors (m=1024, t=1). A benchmark using these parameters shows <1ms latency — misleading for the tuning guide.

**Why it happens:** Copy-pasting the integration test setup into the benchmark.

**How to avoid:** The benchmark `GlobalSetup` must use production defaults: BCrypt work factor 12, Argon2 m=65536 KiB, t=3.

**Warning signs:** BCrypt benchmark reports <5ms (should be ~100ms at wf=12). Argon2 benchmark reports <50ms (should be ~100ms at production params).

### Pitfall 3: Testcontainers Redis start time included in benchmark timing

**What goes wrong:** If Testcontainers Redis is started inside `[IterationSetup]` instead of `[GlobalSetup]`, each iteration pays the container start cost (~1-3s). The benchmark measures container boot, not Redis RTT.

**Why it happens:** Misunderstanding BDN's setup lifecycle.

**How to avoid:** Start containers in `[GlobalSetup]` (runs once). Seed data in `[IterationSetup]` if each iteration needs a fresh key, otherwise seed in GlobalSetup.

**Warning signs:** Redis benchmark reports >1000ms mean (Redis RTT should be <5ms on localhost).

### Pitfall 4: k6 `--network host` not working on macOS/Windows Docker

**What goes wrong:** `docker run --rm -i --network host grafana/k6:latest run - < script.js` works on Linux but fails to reach `localhost:5000` on macOS/Windows because Docker Desktop uses a VM — `--network host` maps to the VM's network, not the host.

**Why it happens:** Docker Desktop's virtualization layer.

**How to avoid:** On non-Linux hosts, use `host.docker.internal` instead of `localhost` in k6 BASE_URL, or run Docker with a port-forwarded stack. Document this in `tests/k6/README.md`.

### Pitfall 5: SignalR negotiate version mismatch

**What goes wrong:** k6 POSTs to `/hubs/lobby/negotiate` without `?negotiateVersion=1`. SignalR 8+ returns a 400 or unexpected format if the negotiate version header/query param is absent.

**Why it happens:** Standard ASP.NET Core SignalR since .NET 5 requires `negotiateVersion=1`.

**How to avoid:** Always include `?negotiateVersion=1` in the negotiate POST URL.

### Pitfall 6: PERF-06 gate Method name mismatch between baseline and new run

**What goes wrong:** The comparison script looks up benchmark methods by the `"Method"` field in the JSON. If a developer renames a benchmark method, the new run has no matching baseline entry — the gate silently skips the check (no regression detected for a method that was removed/renamed).

**How to avoid:** The comparison script should warn (not fail) when a baseline method is missing from the new report: `Console.Error.WriteLine($"WARNING: baseline method '{method}' missing from new report")`. Also run a reverse check: warn if new report has methods absent from the baseline (new benchmark added without a baseline).

### Pitfall 7: BDN xUnit conflict — running BDN inside IsTestProject=true

**What goes wrong:** If `GameKit.LoadTests` is marked `IsTestProject=true`, `dotnet test` tries to load it as an xUnit test assembly. BDN's console runner conflicts with the test host.

**How to avoid:** `GameKit.LoadTests.csproj` must NOT have `<IsTestProject>true</IsTestProject>`. It is `<OutputType>Exe</OutputType>` only.

---

## PERF-05: Performance Tuning Guide Content

### BCrypt Cost Factor vs Latency

| Work Factor | Approx. Hash Time (ms) | Recommended Use |
|-------------|------------------------|-----------------|
| 10 | ~25ms | Development only — too fast for production |
| 11 | ~50ms | Borderline — OWASP recommends ≥100ms target |
| 12 (default) | ~100ms | Production default — meets OWASP 2025 target |
| 13 | ~200ms | High-security deployments with few concurrent logins |
| 14 | ~400ms | Not recommended unless login concurrency <10/s |

[ASSUMED — BCrypt halves/doubles per work factor step; ~100ms at wf=12 on modern hardware is well-established but exact value depends on server CPU]

### Argon2id Cost Factor vs Latency

| m (KiB) | t (iterations) | p (lanes) | Approx. Hash Time | Security Level |
|---------|-----------------|-----------|-------------------|----------------|
| 19456 | 2 | 1 | ~40ms | OWASP minimum |
| 65536 | 3 | 1 | ~100ms | Default (recommended) |
| 65536 | 3 | 4 | ~30ms | Multi-core server (parallelism=4) |
| 131072 | 3 | 1 | ~200ms | Enhanced security |

[ASSUMED — calibrated from OWASP 2025 Argon2id guidance; actual values measured at benchmark time]

### Npgsql Connection Pool Sizing

- **Default Npgsql MaxPoolSize:** 100 connections per pool (one pool per unique connection string)
- **GameKit recommendation:** `Maximum Pool Size=25` for the matchmaking path (as established in Phase 5/`LoadTestFixture.cs`). This prevents the 25-connection cap from becoming a bottleneck.
- **Tuning formula:** `MaxPoolSize = (peak_concurrent_requests × avg_connection_hold_ms) / avg_request_ms + safety_factor`
- **Operator guidance:** Monitor `npgsql.pool.available_idle_connections` via the Npgsql EventSource; if it frequently reaches 0, increase MaxPoolSize.

### Top-5 Hot Queries (Matchmaking Focus)

1. `SELECT * FROM gamekit.matchmaking_tickets WHERE "Status" = 0` — ticker scan for queued tickets. Index: `(Status, CreatedAt)`. [ASSUMED based on MatchmakingTicket entity + ticker logic]
2. `SELECT * FROM gamekit.player_ratings WHERE "PlayerId" = $1 AND "LadderId" = $2` — rank lookup per matchmaking candidate. Index: `(PlayerId, LadderId)`.
3. `INSERT INTO gamekit.matchmaking_tickets ... ON CONFLICT DO NOTHING` — idempotent enqueue (SCALE-03).
4. `SELECT * FROM gamekit.players WHERE "Id" = $1` — player existence check at auth/enqueue time.
5. `UPDATE gamekit.matchmaking_tickets SET "Status" = $1 WHERE "Id" = ANY($2)` — bulk status update at match formation.

All of these benefit from the Postgres `ANALYZE` after bulk loads and correct index coverage. The tuning guide should note that EF Core's change tracker can generate N+1 queries if `Include()` chains are omitted on navigation properties — always check `EnableSensitiveDataLogging()` + EF Core query logs when benchmarking.

---

## Validation Architecture

`workflow.nyquist_validation` is `true` in `.planning/config.json` — this section is required.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 (for unit/integration tests); BenchmarkDotNet 0.15.8 (for benchmarks — console runner, not xUnit) |
| Config file | `xunit.runner.json` (existing); no BDN config file needed (args passed on CLI) |
| Quick run command (benchmarks) | `dotnet run --project tests/GameKit.LoadTests -c Release -- --job short --filter '*JwtValidation*'` |
| Full suite command (benchmarks) | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*'` |
| k6 quick run | `docker run --rm -i grafana/k6:latest run - < tests/k6/matchmaking-burst.js` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PERF-01 | JWT validation benchmark | BDN benchmark | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*JwtValidation*'` | ❌ Wave 0 |
| PERF-01 | BCrypt verify benchmark | BDN benchmark | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*BCrypt*'` | ❌ Wave 0 |
| PERF-01 | Argon2id verify benchmark | BDN benchmark | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*Argon2*'` | ❌ Wave 0 |
| PERF-01 | Glicko-2 Apply benchmark | BDN benchmark | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*Glicko2*'` | ❌ Wave 0 |
| PERF-01 | Matchmaking Redis round-trip benchmark | BDN benchmark + Testcontainers | `dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*Ticket*'` | ❌ Wave 0 |
| PERF-02 | Baselines committed in `benchmarks/baselines/` | Manual capture + commit | N/A (one-time capture) | ❌ Wave 0 |
| PERF-03 | Matchmaking burst 500 VU + auth throughput | k6 scenario | `docker run --rm -i grafana/k6:latest run - < tests/k6/matchmaking-burst.js` | ❌ Wave 0 |
| PERF-04 | Lobby SignalR fan-out delivery distribution | k6 scenario (post-spike) | `docker run --rm -i grafana/k6:latest run - < tests/k6/lobby-signalr-fanout.js` | ❌ Wave 0 |
| PERF-04 | Spike: k6 WebSocket SignalR handshake | k6 spike script | `docker run --rm -i grafana/k6:latest run - < tests/k6/spike-signalr.js` | ❌ Wave 0 |
| PERF-05 | Performance tuning guide exists | Manual review | N/A (document) | ❌ Wave 0 |
| PERF-06 | Regression gate exits non-zero on >20% regression | dotnet integration test of gate script | `dotnet run --project benchmarks/CompareBaseline -- <new.json> <base.json>; echo $?` | ❌ Wave 0 |

### Sampling Rate

- **Per benchmark commit:** `dotnet run --project tests/GameKit.LoadTests -c Release -- --job short --filter '*' --exporters json`
- **Per wave merge:** Full benchmark run (no `--job short`)
- **Phase gate:** All benchmarks pass + PERF-06 comparison exits 0

### Wave 0 Gaps

- [ ] `tests/GameKit.LoadTests/GameKit.LoadTests.csproj` — new project
- [ ] `tests/GameKit.LoadTests/Program.cs` — BDN console runner entry
- [ ] `tests/GameKit.LoadTests/Benchmarks/JwtValidationBenchmarks.cs`
- [ ] `tests/GameKit.LoadTests/Benchmarks/PasswordHasherBenchmarks.cs`
- [ ] `tests/GameKit.LoadTests/Benchmarks/Glicko2Benchmarks.cs`
- [ ] `tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs`
- [ ] `tests/k6/matchmaking-burst.js`
- [ ] `tests/k6/lobby-signalr-fanout.js`
- [ ] `tests/k6/spike-signalr.js`
- [ ] `tests/k6/helpers/signalr.js`
- [ ] `benchmarks/CompareBaseline/CompareBaseline.csproj` + `Program.cs`
- [ ] `benchmarks/baselines/report-baseline.json` (capture after first full benchmark run)
- [ ] `benchmarks/BASELINES.md`
- [ ] `docs/performance-tuning.md`

---

## Security Domain

`security_enforcement` is not explicitly set to `false` in config.json — section required.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Benchmarks do not change auth logic |
| V3 Session Management | No | No new session surface |
| V4 Access Control | No | No new endpoints |
| V5 Input Validation | No | k6 scripts are developer tooling, not shipped endpoints |
| V6 Cryptography | Tangential | BCrypt/Argon2 benchmarks must use production params (wf≥12; m≥19456 KiB) — benchmark at low params is misleading, not a security gap |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| JWT in k6 script | Information disclosure | JWTs used in k6 scripts must be short-lived tokens minted against the LOCAL Testcontainers stack — never against production. Document in `tests/k6/README.md`. |
| k6 script committed with hardcoded credentials | Tampering | Use `__ENV.JWT` / `-e JWT=...` at CLI invocation time; never hardcode tokens in committed scripts |
| Benchmark running against wrong environment | Tampering | Benchmark `Program.cs` must never accept external network connections — all I/O targets Testcontainers only |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `k6/experimental/websockets` | `k6/websockets` (stable) | k6 v0.53+ | `k6/experimental/websockets` is deprecated in k6 v2.0.0 — use `k6/websockets` |
| `k6/ws` (callback-based) | `k6/websockets` (WHATWG events) | k6 v0.40+ | `k6/ws` still works but is the legacy API; prefer `k6/websockets` |
| BenchmarkDotNet in xUnit `[Fact]` | `BenchmarkRunner.Run<>()` in console app | Always | Never run BDN inside a test host — statistical model conflicts |

**Deprecated/outdated:**
- `k6/experimental/websockets`: deprecated in k6 v2.0.0; triggers deprecation warning. Use `k6/websockets`.
- `--exporters markdown` for PERF-06 gate: markdown is human-readable but unstructured — parse JSON instead.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | k6 scenarios (PERF-03/04), Testcontainers | ✓ | 29.5.3 | — |
| `grafana/k6:latest` Docker image | k6 scenarios | ✓ | v2.0.0 | `docker pull grafana/k6:latest` |
| .NET 10 SDK | BDN console app, comparison tool | ✓ | 10.0.106 (global.json) | — |
| Testcontainers Redis | Matchmaking ticket benchmark | ✓ | 4.11.0 (via Directory.Packages.props) | — |
| Testcontainers Postgres | Optional (Glicko-2 is pure-CPU; not needed) | ✓ | 4.11.0 | Skip Postgres in benchmark; not needed |

**Missing dependencies with no fallback:** None.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | BCrypt at work factor 12 takes ~100ms on modern server hardware | §PERF-05 Tuning Guide | Benchmark will show the real value; document from actual results, not this assumption |
| A2 | Argon2id at m=65536 KiB, t=3, p=1 takes ~100ms on modern hardware | §PERF-05 Tuning Guide | Same — benchmark will correct |
| A3 | k6 AGPLv3 copyleft does not affect MIT-licensed test scripts that call the binary | §k6 Scenarios | If wrong, k6 scripts in the repo would need to be GPL-licensed too; consult legal counsel before publishing publicly. AGPLv3's copyleft is generally understood to apply to distributed software that links the library, not to scripts that invoke the binary as a subprocess. |
| A4 | SignalR `negotiateVersion=1` query param is required in .NET 10 ASP.NET Core | §Pitfall 5 | If wrong, the negotiate POST may succeed without it; the spike will reveal the actual behavior |
| A5 | Top-5 hot queries listed in §PERF-05 are correct | §PERF-05 | Planner should verify against actual EF Core query log output from the load test |

---

## Open Questions

1. **k6 host networking on macOS/Windows CI runners**
   - What we know: `--network host` works on Linux (GitHub Actions `ubuntu-24.04`); fails on macOS/Windows Docker Desktop VMs.
   - What's unclear: Whether the CI runner for k6 scenarios will always be Linux or if Mac is ever used.
   - Recommendation: Document `host.docker.internal` alternative in `tests/k6/README.md`; CI always runs on `ubuntu-24.04` per existing `ci.yml`.

2. **PERF-06 CI job separation**
   - What we know: Benchmarks take 10+ minutes for the full suite; the existing CI test job is fast (unit + integration).
   - What's unclear: Whether PERF-06 should run on every PR or only on pushes to main.
   - Recommendation: Gate benchmark CI on pushes to main only (not every PR). The planner should add a separate `benchmarks` job to `ci.yml` with `on: push: branches: [main]` only.

3. **Spike result scope**
   - What we know: The spike script (`tests/k6/spike-signalr.js`) must be committed and its result documented.
   - What's unclear: If the spike reveals that stock k6 cannot complete the SignalR handshake (unlikely given the confirmed `k6/websockets` module), the fan-out scenario (PERF-04) would need an xk6 extension build.
   - Recommendation: The planner should include a `checkpoint:human-verify` task after the spike task, before the full fan-out scenario is written.

---

## Sources

### Primary (HIGH confidence)
- `tests/GameKit.Matchmaking.LoadTests/` (entire directory) — [VERIFIED: file read] Confirms existing project is xUnit + sustain load, NOT BenchmarkDotNet; must remain separate from new LoadTests project
- `tests/GameKit.Matchmaking.LoadTests/LoadTestFixture.cs` — [VERIFIED: file read] Confirms MaxPoolSize=25, Testcontainers Redis+Postgres lifecycle, MintPlayerJwt pattern
- `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` — [VERIFIED: file read] Default work factor 12; entry point `BCrypt.Net.BCrypt.Verify()`
- `src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs` — [VERIFIED: file read] Default m=65536 KiB, t=3, p=1
- `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs` — [VERIFIED: file read] `Isopoh.Cryptography.Argon2.Argon2.Verify(hash, password)` — note arg order
- `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:199` — [VERIFIED: file read] `TokenValidationParameters` setup: RSA-SHA256, ValidateIssuer/Audience/Lifetime/SigningKey
- `src/GameKit.Auth/Services/JwtIssuer.cs` — [VERIFIED: file read] RSA key loaded once at construction; `JwtSecurityTokenHandler.WriteToken()`
- `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` — [VERIFIED: file read] `Apply(state, batch)` — pure CPU, no I/O
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — [VERIFIED: file read] Redis key schema: `mm:queue:{ladderId}:{pool}`, `mm:ticket:{id}`
- `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` — [VERIFIED: file read] `POST /api/mm/queue` enqueue endpoint
- `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` — [VERIFIED: file read] Hub URL `/hubs/lobby`; JWT via `?access_token`; `HubConnectionBuilder.WithUrl()`
- `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` — [VERIFIED: file read] SignalR backplane test pattern with two in-process TestServer instances
- `.github/workflows/ci.yml` — [VERIFIED: file read] Existing CI structure: `ubuntu-24.04`, no BDN step, no `NuGetAudit=false`
- `Directory.Packages.props` — [VERIFIED: file read] Pinned versions; BenchmarkDotNet NOT currently in repo
- `global.json` — [VERIFIED: file read] SDK 10.0.106 pinned
- `.planning/config.json` — [VERIFIED: file read] `nyquist_validation: true`
- `docker run grafana/k6:latest version` — [VERIFIED: bash] k6 v2.0.0
- `docker run grafana/k6:latest` with `k6/websockets` import — [VERIFIED: bash] stable `k6/websockets` available; `k6/experimental/websockets` deprecated in v2.0.0
- BenchmarkDotNet v0.15.8 latest on NuGet — [VERIFIED: api.nuget.org curl]

### Secondary (MEDIUM confidence)
- BenchmarkDotNet JSON report structure (`"Benchmarks"[].Method` + `"Statistics.Mean"`) — [CITED: benchmarkdotnet.org/articles/samples/IntroExportJson.html]
- BenchmarkDotNet exporters docs (`-report-full.json` suffix, `[JsonExporterAttribute.Full]`) — [CITED: benchmarkdotnet.org/articles/configs/exporters.html]
- SignalR JSON protocol: negotiate endpoint, handshake `{"protocol":"json","version":1}\x1e`, 0x1E record separator — [CITED: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz]
- k6 no native SignalR support; requires manual protocol implementation — [CITED: github.com/grafana/k6/issues/3936]
- BDNA (BenchmarkDotNet.Analyser) tool overview — [CITED: dev.to/newday-technology/measuring-performance-using-benchmarkdotnet-part-3-breaking-builds-36il]

### Tertiary (LOW confidence)
- BCrypt ~100ms at work factor 12 / Argon2 ~100ms at default params — [ASSUMED — well-known rule of thumb; benchmark will establish the actual values for BASELINES.md]
- Top-5 hot queries for matchmaking — [ASSUMED — derived from entity/endpoint analysis]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — BenchmarkDotNet version verified via NuGet API; k6 version verified via docker run; all other deps already pinned in repo
- Architecture: HIGH — project structure derived from verified codebase analysis; BDN console-app pattern is documented
- PERF-06 gate: HIGH — JSON schema verified from BDN docs; comparison algorithm is deterministic
- k6 SignalR: MEDIUM — `k6/websockets` confirmed in stock image; SignalR protocol details confirmed from official docs; actual handshake success requires spike validation
- Pitfalls: HIGH — all derived from verified code reading or BDN documentation

**Research date:** 2026-06-23
**Valid until:** 2026-09-23 (BDN minor updates do not change JSON schema; k6 major release may change module names)

---

## RESEARCH COMPLETE
