# Code Review: Unit tests for PackingStatsTile failure isolation

## Summary
The implementation adds three `[Fact]` unit tests covering all four functional requirements from the specification: the happy path with successful Shoptet enrichment (FR-1), graceful degradation when Shoptet fails (FR-2), null-packer-name fallback to "Neznámý" (FR-3 folded into FR-1), and total failure when the repository fails (FR-4). The test file correctly mirrors the source namespace structure, uses established patterns from sibling tile and handler tests, and exercises the two independent failure paths plus the happy path without modifying production code.

## Review Result: PASS

### task: add-packingstatstile-unit-tests

**Status:** PASS

**Spec Compliance:**
- ✓ FR-1 (happy path): Test 1 covers all acceptance criteria — both dependencies succeed, status is "success", counts and packer breakdown are correctly mapped, `lastSync` is non-null.
- ✓ FR-2 (Shoptet isolation): Test 2 verifies the inner catch — Shoptet throws, but status remains "success", only the Shoptet-derived fields are null, packer data is fully populated (isolation proven), and the exception does not propagate.
- ✓ FR-3 (null-packer-name fallback): Correctly folded into Test 1 per arch-review Decision 3 — one packer with `PackedBy = null` asserts `packerName == "Neznámý"` on line 96.
- ✓ FR-4 (repository failure): Test 3 covers outer catch — status is "error", error message matches the expected Czech string, `data` property is **absent** (not null, critical distinction), and no exception propagates.
- ✓ NFR-1 (performance): Pure in-memory mocks, no I/O, deterministic.
- ✓ NFR-2 (security): No auth, secrets, or PII involved.
- ✓ NFR-3 (determinism): Uses a custom `FakeTimeProvider : TimeProvider` subclass with fixed Prague timezone (+2 hours), making `GetLocalNow()` deterministic.

**Architecture Adherence:**
- ✓ **Decision 1 honored**: Asserts against serialized JSON using `JsonDocument` and `ToJsonDoc` helper, matching the `FailedJobsTileTests` pattern (lines 78, 123, 156).
- ✓ **Decision 2 honored**: Uses nested `private sealed class FakeTimeProvider : TimeProvider` subclass (lines 21-35), not `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, maintaining consistency with `GetPackingDashboardHandlerTests` and avoiding UTC-default timezone footgun.
- ✓ **Decision 3 applied**: FR-3 folded into FR-1 as permitted (arch-review Decision 3, line 50 comment), reducing test count to three while still covering all logical paths.
- ✓ **Amendment 2 applied**: FR-4 error shape correctly omits `data` property entirely; assertion uses `TryGetProperty("data", out _).Should().BeFalse()` on line 161, avoiding the copy-paste trap of checking for null.
- ✓ **Amendment 3 applied**: New `DashboardTiles/` folder created in test project structure, mirroring the source layout and matching the `BackgroundJobs/DashboardTiles/` precedent.

**Test Quality:**
- ✓ Test 1 (`LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers`): Comprehensive happy path with two packer entries (one named, one null), assertions cover status, all count fields, timestamp field kind, total, and per-packer mapping. Null-packer-name assertion on line 96 closes FR-3.
- ✓ Test 2 (`LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated`): Properly isolates the inner catch. Repository succeeds, first Shoptet call throws `HttpRequestException`, test confirms status stays "success" and only the three Shoptet-derived fields serialize as JSON null (lines 128–130), while packer data remains fully intact (lines 132–137). Proves the two paths are independent.
- ✓ Test 3 (`LoadDataAsync_RepositoryThrows_ReturnsError`): Outer catch covered. Repository throws, status is "error", Czech error message matches, `data` property is correctly absent. No exception escapes.
- ✓ All tests use proper AAA (Arrange-Act-Assert) structure, clear naming, and inline comments (FR-1, FR-2, FR-4) explaining what each test covers.

**Source Code Verification:**
- ✓ Tested source (`PackingStatsTile.cs` lines 36–105) is unchanged; test file is test-only.
- ✓ Constructor signature matches (line 24–28 of source, line 40–45 of test).
- ✓ Inner catch exists (source lines 51–60) and is exercised by Test 2.
- ✓ Outer catch exists (source lines 95–103) and is exercised by Test 3.
- ✓ Null-coalesce for `PackedBy` is on line 66 of source and tested by Test 1.

**File Location & Namespace:**
- ✓ File: `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs` — correct path with new folder.
- ✓ Namespace: `Anela.Heblo.Tests.Features.Packaging.DashboardTiles` — mirrors source namespace structure.
- ✓ Using directives correct: all namespaces present for `PackingStatsTile`, `IPackageRepository`, `IPackingOrderClient`, `PackerPackingSummary`, and `System.Text.Json`.

**Dependencies:**
- ✓ All test dependencies (`xUnit`, `Moq`, `FluentAssertions`, `NullLogger`, `FakeTimeProvider` pattern) already present in `Anela.Heblo.Tests.csproj`.
- ✓ No new packages, no `.csproj` changes needed.

**Completeness:**
- ✓ All four functional requirements covered (three tests, FR-3 folded into FR-1).
- ✓ All non-functional requirements met.
- ✓ No production code touched.
- ✓ Ready for `dotnet build`, `dotnet format`, and `dotnet test`.

## Overall Notes

The implementation is thorough and precise. The tests are characterization tests over already-correct production code, so they pass on the first run without requiring any fixes to the source. The test suite successfully pins the two independent failure paths (graceful degradation and total failure) plus the happy path, protecting the dashboard's API contract against refactors that could silently change the response shape. The code follows the established patterns in the codebase (FailedJobsTileTests, GetPackingDashboardHandlerTests) and adheres to every spec and architectural guidance without deviation. No concerns.
