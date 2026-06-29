# Specification: Coverage Verification — ScanPackingOrderHandler Gaps

## Summary

The weekly coverage-gap routine flagged `ScanPackingOrderHandler` at 32.9% line coverage (threshold: 60%) against CI run `23c3b5d571c976074ee31869c96e29487098040c`. On inspection, all three described coverage gaps (zero-weight fallback, packer eligibility guard, and the backfill-existing-shipment path) already have passing unit tests in the codebase at that same commit. The task is therefore to diagnose and fix the coverage measurement rather than to write new tests.

## Background

The coverage-gap routine filed this issue after a CI coverage report showed `ScanPackingOrderHandler.cs` at 32.9%. Three specific branches were identified as untested. However, source-code inspection of the three test files at the referenced commit (`23c3b5d`) shows:

- `Handle_AllItemsHaveZeroWeight_UsesFallbackPackageWeight` exists in `ScanPackingOrderHandlerTests` (line 187) and asserts `WeightGrams = 1000`.
- `Handle_WithUnknownPackingUserId_ReturnsPackingUserNotEligible` and `Handle_WithIneligiblePackingUser_ReturnsPackingUserNotEligible` exist in `ScanPackingOrderPackerTests` (lines 132, 160) and cover the null, inactive, and CanPack=false sub-cases via `[Theory]`.
- `Handle_BackfillsPackages_WhenEligibleShipmentAlreadyExisted` exists in `ScanPackingOrderHandlerPackagePersistenceTests` (line 117) and verifies both `AddMissingAsync` and the `MarkAsPackedAsync` call through the companion test `Handle_LabelsExist_MarksOrderAsPacked` in `ScanPackingOrderHandlerTests`.

The 32.9% figure is therefore inconsistent with the tests that exist. The most likely cause is a coverage-tool misconfiguration (excluded test projects, stale coverage artifact, or an assembly-filter pattern that omitted one or more test DLLs from the run).

## Functional Requirements

### FR-1: Reproduce the low coverage measurement

Run the coverage tool locally against the three test assemblies in the same configuration used by CI run `#28295125598` to confirm or refute the 32.9% figure.

**Acceptance criteria:**
- The coverage command is executed and produces a percentage for `ScanPackingOrderHandler.cs`.
- If the result differs significantly from 32.9%, the discrepancy is documented with the exact command, coverage tool version, and filter flags used.

### FR-2: Identify the root cause of the measurement gap

Determine why the coverage tool under-counted. Candidate causes to investigate, in order of likelihood:

1. **Filter pattern excludes test projects**: the coverage run uses `--filter` or `--include` that inadvertently excludes `Anela.Heblo.Tests` or one of the three test namespaces (`Anela.Heblo.Tests.Application.Packaging`, `Anela.Heblo.Tests.Features.Packaging`).
2. **Stale artifact**: the CI run collected coverage from a build artifact that predates the test additions, even though the commit SHA shows all tests present (all three test files were last modified in commit `936bde8` or `42ea377`, both of which predate `23c3b5d`).
3. **Test project not included in the coverage run**: only one of the three test projects was instrumented.
4. **Assembly-level vs. project-level coverage**: the handler file is part of `Anela.Heblo.Application`; a misconfigured `--source-dirs` or `--include-assemblies` flag could exclude it from the denominator.

**Acceptance criteria:**
- The root cause is identified and documented.
- The exact misconfiguration (command flag, YAML step, or tooling setting) is named.

### FR-3: Fix the coverage measurement

Correct the CI configuration so that the coverage report accurately reflects the tests that exist.

**Acceptance criteria:**
- The coverage figure for `ScanPackingOrderHandler.cs` rises to ≥ 60% (the declared threshold) when all three test files are included in the run.
- CI run passes without a coverage-threshold failure for this file.
- No existing tests are removed or disabled.

### FR-4: Verify the existing tests are correct and sufficient

As a secondary check while the coverage tooling is being fixed, confirm the three identified tests actually exercise the described branches correctly and that assertions are strong enough to catch regressions.

**Sub-checks:**

**FR-4a — Zero-weight fallback (`Handle_AllItemsHaveZeroWeight_UsesFallbackPackageWeight`)**
- Items: two items both with `WeightGrams = 0`.
- Settings: uses `DefaultLabelSettings` (no explicit `FallbackPackageWeightGrams`), which means the default value of `1000` from `ShipmentLabelsSettings` applies.
- Assertion: `captured!.Package.WeightGrams.Should().Be(1000)` — correct.
- The test does NOT verify the warning log. This is acceptable (log assertions add fragility); not a gap.
- Status: **sufficient**.

**FR-4b — Packer not found (`Handle_WithUnknownPackingUserId_ReturnsPackingUserNotEligible`)**
- `_authRepo.GetUserByIdAsync` returns `null`.
- Asserts `ErrorCode == PackingUserNotEligible` and `ReplacePackagesForOrderAsync` is never called.
- Status: **sufficient**.

**FR-4c — Packer inactive or CanPack=false (`Handle_WithIneligiblePackingUser_ReturnsPackingUserNotEligible`)**
- `[Theory][InlineData(false, true)][InlineData(true, false)]` covers `IsActive=false,CanPack=true` and `IsActive=true,CanPack=false` independently.
- Asserts `ErrorCode == PackingUserNotEligible`.
- Note: the case `IsActive=false, CanPack=false` (both false) is not a separate inline data row, but the handler's condition is `packer.IsActive && packer.CanPack` — the two existing rows already prove each individual flag is enforced. Not a gap.
- Status: **sufficient**.

**FR-4d — Backfill path (`Handle_BackfillsPackages_WhenEligibleShipmentAlreadyExisted`)**
- Eligible order, existing labels present.
- Asserts `AddMissingAsync` is called once with correct package data.
- `TryMarkAsPackedAsync` is verified separately in `Handle_LabelsExist_MarksOrderAsPacked`.
- Status: **sufficient**.

**Acceptance criteria:**
- All four sub-checks pass manual review with no additional test changes required (unless FR-4 review reveals a genuine assertion gap not visible in the current code read).

## Non-Functional Requirements

### NFR-1: CI stability

The coverage fix must not increase CI run time by more than 30 seconds. Adding a new test project to an existing coverage pass is acceptable; restructuring the entire test pipeline is out of scope.

### NFR-2: Accuracy of the coverage threshold

The corrected coverage measurement must be deterministic across CI runs (same commit → same percentage ± 1%). Flaky coverage figures must not be introduced.

## Data Model

No data model changes. This task touches only test tooling and CI configuration.

## API / Interface Design

No API changes. The handler's public interface (`IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>`) is unchanged.

## Dependencies

- Existing test infrastructure: xUnit, Moq, FluentAssertions — all already in use.
- Coverage tooling: identify the exact tool (Coverlet, dotnet-coverage, or ReportGenerator) and its version from the CI YAML before making changes.
- CI YAML: locate the coverage step in `.github/workflows/` (or equivalent) that produced run `#28295125598`.

## Out of Scope

- Writing new unit tests for `ScanPackingOrderHandler`. The existing tests already cover the flagged branches.
- Raising coverage for other files in the Packaging module.
- Modifying handler business logic.
- E2E test coverage for the packing scan flow.

## Open Questions

1. **Which coverage tool and version is used in CI?** The CI YAML step for coverage collection needs to be identified before the root cause can be confirmed. Coverlet, dotnet-coverage, and OpenCover each have different filter syntax and failure modes.
2. **Is the 32.9% figure per-file or per-assembly?** If per-assembly, the denominator may include other handler files in the Packaging module that genuinely lack tests, pulling the average down below the per-file threshold for this specific handler.
3. **Are all three test projects (`Anela.Heblo.Tests` at both paths) included in the same `dotnet test` invocation?** The two test namespaces live in different directory paths (`backend/test/Anela.Heblo.Tests/Application/Packaging/` and `backend/test/Anela.Heblo.Tests/Features/Packaging/`) but appear to be in the same assembly. Confirm this so the fix targets the right scope.

## Status: HAS_QUESTIONS
