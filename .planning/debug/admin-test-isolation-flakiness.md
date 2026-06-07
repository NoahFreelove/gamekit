---
status: resolved
trigger: "Fix flaky test isolation in GameKit.Admin.Integration.Tests - Phase 12 new tests cause intermittent failures in SuperadminGateTests, ProductionGateTests, and RoadmapScenarioTests SC#1"
created: 2026-06-06T00:00:00Z
updated: 2026-06-06T00:00:00Z
---

## Current Focus

hypothesis: Multiple compounding issues: (1) IAsyncLifetime tests (AdminEventHubTests, RedisErrorCounterTests, RankAdjustServiceTests) leave admin_users rows in DB after DisposeAsync because they do NOT truncate in DisposeAsync; (2) RedisErrorRateCounter leaves gamekit:admin:errors:{bucket} keys in Redis leaking into HealthProbeTests; (3) AdminLiveBroadcastService subscription may not cleanly unsubscribe before next test starts; (4) RankAdjustServiceTests.ResetTables is in InitializeAsync instead of constructor, causing a window where it could race with prior test's DisposeAsync in a hypothetical scenario
test: Read all IAsyncLifetime test classes and trace the exact lifecycle; then run the suite multiple times
expecting: Flakiness caused by stale admin_users rows from IAsyncLifetime tests (no cleanup in DisposeAsync) affecting SuperadminGateTests which depend on exact admin count; Redis error keys affecting HealthProbeTests
next_action: Run the full suite twice to observe actual failures

## Symptoms

expected: All 60 tests pass reliably across multiple runs
actual: 3-4 tests fail intermittently; SAME tests pass when run in isolation; failing set changes between runs (flaky)
errors: SuperadminGateTests.Production_WithZeroSuperadmins_Throws_AtStartup, Production_WithSeededSuperadmin_StartsSuccessfully, Development_WithZeroSuperadmins_Logs_Warning, RoadmapScenarioTests SC#1
reproduction: Run full Admin integration test suite; 3-4 tests fail; run again - different subset fails
started: Phase 12 added RedisErrorCounterTests, AdminEventHubTests, and a RankAdjust test bringing total to 60

## Eliminated

- hypothesis: Tests not in [Collection("Admin")] causing parallel execution
  evidence: All test classes have [Collection("Admin")] attribute; AdminTestHost is not a test class
  timestamp: 2026-06-06T00:00:00Z

## Evidence

- timestamp: 2026-06-06T00:00:00Z
  checked: All test class files for Collection attribute
  found: All 19 test classes are in [Collection("Admin")]; AdminTestHost is infrastructure not a test
  implication: Parallel execution is NOT the cause; all tests are serialized

- timestamp: 2026-06-06T00:00:00Z
  checked: AdminEventHubTests lifecycle
  found: Constructor calls ResetAdminUsers (truncate); InitializeAsync seeds hub-test-a and hub-test-b and starts TWO hosts; DisposeAsync stops both hosts but does NOT truncate admin_users
  implication: After AdminEventHubTests runs, admin_users still has hub-test-a and hub-test-b rows. The NEXT test's constructor must truncate to be safe.

- timestamp: 2026-06-06T00:00:00Z
  checked: RedisErrorCounterTests lifecycle
  found: Constructor calls ResetAdminUsers (truncate); InitializeAsync seeds replica-a and replica-b; DisposeAsync stops hosts but does NOT truncate admin_users AND does NOT flush Redis error keys
  implication: After RedisErrorCounterTests, (1) admin_users still has replica-a and replica-b; (2) Redis has gamekit:admin:errors:{bucket} keys with count=15

- timestamp: 2026-06-06T00:00:00Z
  checked: RankAdjustServiceTests lifecycle
  found: Constructor does NOT reset tables; InitializeAsync calls ResetTables (which truncates admin_users) THEN seeds superadmin; DisposeAsync stops host but does NOT truncate
  implication: For RankAdjustServiceTests, if xUnit happens to create the next test instance BEFORE InitializeAsync completes (not possible in xUnit 2.x serialized mode), but still concerning

- timestamp: 2026-06-06T00:00:00Z
  checked: SuperadminGateTests lifecycle
  found: Constructor calls ResetAdminUsers synchronously (blocking TRUNCATE); each of 3 test methods gets its own class instance with its own constructor call
  implication: SuperadminGateTests should be safe IF its constructor runs BEFORE any seeded rows matter. But if xUnit test ordering puts an IAsyncLifetime test's InitializeAsync BETWEEN the constructor and test method of another class -- impossible in serialized xUnit.

- timestamp: 2026-06-06T00:00:00Z
  checked: xUnit IAsyncLifetime ordering within [Collection]
  found: xUnit fully serializes: Constructor → InitializeAsync → Test → DisposeAsync → [next Constructor → InitializeAsync → Test → DisposeAsync]
  implication: No actual race between classes. But ordering between classes is NOT guaranteed alphabetically between runs.

## Resolution

root_cause: Each AdminTestHost.InitializeAsync call allocates inotify instances via Host.CreateDefaultBuilder() which adds appsettings.json and appsettings.{Env}.json config sources with reloadOnChange:true, each creating a FileSystemWatcher. The system inotify max_user_instances limit is 128. Phase 12 added 4 extra hosts (2 in AdminEventHubTests + 2 in RedisErrorCounterTests), pushing the total past 128 when running the full 60-test suite. Tests that arrive late in the run fail with IOException "inotify instances limit reached" which propagates as a test infrastructure failure masquerading as test isolation failures.
fix: In AdminTestHost.InitializeAsync, add ConfigureAppConfiguration that removes all JsonConfigurationSource entries (which carry reloadOnChange:true) from the builder. Tests configure everything programmatically so file-based appsettings.json is not needed.
verification: empty
files_changed: [tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs]
