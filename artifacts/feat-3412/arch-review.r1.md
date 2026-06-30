# Architecture Review: Coverage Measurement Fix — ScanPackingOrderHandler

## Skip Design: true

## Architectural Fit Assessment

This is a CI tooling fix, not a product feature. It touches the coverage measurement pipeline only — no application code, no domain logic, no UI. The fix integrates cleanly into the existing CI infrastructure: both `ci-main-branch.yml` and `ci-feature-branch.yml` use identical `dotnet test` invocations, so the same fix applies to both.

The weekly coverage-gap routine (`docs/routines/weekly-coverage-gap.md`) is the consumer of the CI-produced `coverage-backend` artifact. It parses multiple Cobertura XML files, one per test project, and appears to evaluate each file independently — meaning a file covered in one XML but absent from another may be measured at a lower effective coverage if the routine picks the "worst" XML rather than merging them. The root cause diagnosis must confirm which of two candidate mechanisms produced the 32.9% figure.

## Proposed Architecture

### Component Overview

```
dotnet test Anela.Heblo.sln (solution-level run)
│
├── Anela.Heblo.Tests.csproj ──────────────────── coverlet.collector 6.0.2
│   ├── ScanPackingOrderHandlerTests              (hits handler via Application ref)
│   ├── ScanPackingOrderPackerTests               (hits handler via Application ref)
│   └── Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests
│
├── Anela.Heblo.Adapters.Shoptet.Tests.csproj ── coverlet.collector 6.0.0
│   └── (no ScanPackingOrderHandler tests)
│
├── Anela.Heblo.Adapters.Flexi.Tests.csproj ───── coverlet.collector 6.0.0
│   └── (no ScanPackingOrderHandler tests)
│
├── Anela.Heblo.Adapters.HomeAssistant.Tests ──── coverlet.collector 6.0.0
│   └── (no ScanPackingOrderHandler tests)
│
├── Anela.Heblo.Adapters.OpenMeteo.Tests ──────── coverlet.collector 6.0.0
│   └── (no ScanPackingOrderHandler tests)
│
└── Anela.Heblo.Adapters.Plaud.Tests.csproj ───── NO coverlet.collector installed
    └── (no ScanPackingOrderHandler tests)

CI artifact: coverage/**/*.cobertura.xml  (one XML file per test project run)

Weekly routine
└── Downloads coverage-backend artifact
    └── Parses multiple Cobertura XMLs for per-file line coverage
        └── Files issue if any file's coverage < 60%
```

### Key Design Decisions

#### Decision 1: Root Cause — Per-Project XML Fragmentation Without Merging

**Options considered:**
1. Coverage filter excludes the test project accidentally
2. Stale artifact from a prior commit (pre-test)
3. Routine reads only one XML (e.g., alphabetically first) and misses the rest
4. Routine merges XMLs but ScanPackingOrderHandler.cs only appears in one XML with partial results because Coverlet attributes coverage to the assembly that exercises the code, and other assemblies with no tests for this class generate a zero-coverage entry

**Chosen approach:** Candidate root cause is option 3 or 4.

Here is the key structural fact: `dotnet test Anela.Heblo.sln --collect:"XPlat Code Coverage"` generates one `coverage.cobertura.xml` per test project, each placed under a unique GUID subdirectory inside `./coverage/`. There is **no merge step** anywhere in the CI YAML. The CI uploads the glob `coverage/**/*.cobertura.xml` as the `coverage-backend` artifact, producing multiple XML files.

Each Coverlet-generated XML only contains coverage data for the assemblies actually exercised by that test project. `ScanPackingOrderHandler.cs` lives in `Anela.Heblo.Application`. The Shoptet, Flexi, HomeAssistant, OpenMeteo, and Plaud test projects have no reference to `Anela.Heblo.Application` and will therefore either not mention the file at all or list it with 0 hits. `Anela.Heblo.Tests` is the only project that references `Anela.Heblo.Application` and exercises the handler.

If the weekly routine evaluates coverage per-file by taking the **first XML that mentions a file** (or the lowest value seen across XMLs), and if any non-`Anela.Heblo.Tests` XML lists `ScanPackingOrderHandler.cs` with partial or zero coverage, the routine will report a lower figure than reality.

The more likely variant is that the routine sees **multiple XMLs**, some of which are from adapter test projects that load the application assembly transitively but run no ScanPackingOrder tests, producing an XML entry with only the lines exercised by shared startup code. The routine then reads the worst single-file figure (or fails to merge line hits across XMLs) and reports 32.9%.

**Rationale:** There is no evidence of a filter misconfiguration — the CI command is identical in both workflows, and the test project references confirm all three test files are in `Anela.Heblo.Tests`. The path-rewrite step (`sed -i 's|filename="|filename="backend/src/|g'`) is applied to all XMLs before upload, so the file paths are consistent. The simplest explanation for 32.9% — when a handler has 13+ tests covering every branch — is that the routine processes multiple XMLs for the same source file and uses a non-aggregated figure from a sparse XML.

#### Decision 2: Fix Strategy — Merge Before Upload, Not After

**Options considered:**
A. Add a ReportGenerator merge step in CI before uploading the artifact
B. Update the weekly routine to aggregate line hits across all XMLs for the same source file before computing percentage
C. Configure Coverlet with `--include` / `--exclude` so only `Anela.Heblo.Tests` produces an XML that mentions application sources

**Chosen approach:** Option A — add a `dotnet-reportgenerator-globaltool` merge step in both CI workflows immediately after the existing coverage processing step, before the artifact upload. The merged output replaces the multi-XML artifact (or is uploaded alongside as a separate named file that the routine prioritizes).

**Rationale:** Option B requires modifying the routine's parsing logic, which runs in a remote Claude Code context and is not easily testable. Option C is fragile — `--include` patterns in Coverlet can break silently when assemblies are renamed. Option A is standard practice: `reportgenerator -reports:"coverage/**/*.cobertura.xml" -targetdir:"./coverage/merged" -reporttypes:Cobertura` produces a single, fully merged XML that correctly aggregates line hits across all test projects. This is deterministic, locally reproducible, and independent of how the routine does its parsing.

The routine documentation states it downloads `coverage/**/*.cobertura.xml`. If a merged file is placed at a predictable path (e.g., `coverage/merged/coverage.cobertura.xml`), the routine will pick it up via the existing glob. The individual per-project XMLs remain in the artifact for backward compatibility with CodeCov.

## Implementation Guidance

### Directory / Module Structure

No new files in the application. Changes are confined to:

- `.github/workflows/ci-main-branch.yml` — backend-tests job
- `.github/workflows/ci-feature-branch.yml` — backend-tests job

Both files are structurally identical in their coverage collection steps; the same change applies to both.

### Interfaces and Contracts

The weekly routine's artifact consumption contract is `coverage/**/*.cobertura.xml`. The merged output file must match this glob. The path `coverage/merged/Cobertura.xml` (ReportGenerator's default output name) satisfies it.

The `coverage-backend` upload step in both workflows uses `path: coverage/**/*.cobertura.xml`. This glob will pick up the merged file automatically alongside the per-project files — no upload step change needed.

### Data Flow

**Current (broken) flow:**
```
dotnet test → 6 per-project XMLs → sed path fix → artifact upload
→ routine downloads artifact → reads XMLs one by one → ScanPackingOrderHandler.cs
  appears in Anela.Heblo.Tests XML at ~80%+ AND in adapter XMLs at low/zero %
  → routine reports 32.9% (non-aggregated figure)
```

**Fixed flow:**
```
dotnet test → 6 per-project XMLs → sed path fix
→ reportgenerator merge → single coverage/merged/Cobertura.xml (aggregated)
→ artifact upload (individual + merged)
→ routine downloads artifact → reads merged XML → ScanPackingOrderHandler.cs
  appears once with aggregated line coverage ≥ 60% → no issue filed
```

**Exact CI step to insert** (after "Process coverage files for CodeCov", before "Prepare coverage file list"):

```yaml
- name: 📊 Merge coverage reports
  run: |
    dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.2.5
    reportgenerator \
      -reports:"./coverage/**/*.cobertura.xml" \
      -targetdir:"./coverage/merged" \
      -reporttypes:Cobertura \
      -verbosity:Warning
```

This step must be inserted in **both** `ci-main-branch.yml` and `ci-feature-branch.yml` at the same position in the backend-tests job. The tool installation adds ~10–15 seconds to the job; the merge itself on a codebase of this size adds under 5 seconds.

### Verification of the Fix

After the fix is merged to a branch and CI runs:
1. Download the `coverage-backend` artifact
2. Confirm `coverage/merged/Cobertura.xml` exists
3. Search the merged XML for `ScanPackingOrderHandler.cs` (after the `backend/src/` path rewrite)
4. Confirm `line-rate` attribute is ≥ 0.60
5. On the next weekly routine run (or a manual trigger), confirm no issue is re-filed for this file

### Prerequisite: Confirm the Hypothesis Locally

Before implementing the fix, run this locally to confirm the root cause:

```bash
dotnet test Anela.Heblo.sln \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage-local \
  --filter "Category!=Playwright&Category!=Integration" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

# Count XMLs
find ./coverage-local -name "*.cobertura.xml" | wc -l

# Find ScanPackingOrderHandler entries in each XML
for f in $(find ./coverage-local -name "*.cobertura.xml"); do
  echo "=== $f ==="
  grep -A2 "ScanPackingOrderHandler" "$f" || echo "(not mentioned)"
done
```

If the handler appears with low line-rate in any adapter XML, the hypothesis is confirmed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| ReportGenerator tool install fails in CI (network, version pinning) | Medium | Pin to a specific version (`5.2.5`); use `--ignore-failed-sources` flag; add `continue-on-error: false` so CI fails fast rather than silently skipping the merge |
| Merged XML has wrong path prefix (routine can't match to source files) | Medium | The `sed` path-rewrite step runs on per-project XMLs before the merge; ReportGenerator merges the already-rewritten paths. Verify in a test run before closing. |
| CI job runtime exceeds NFR-1 (+30 sec budget) | Low | Tool install ~10s + merge ~5s = ~15s total. Within budget. |
| Merged XML reports genuinely low coverage for a different file, masking a real gap | Low | This is correct behaviour — the merged figure is accurate. If a file is genuinely below 60%, the routine should file an issue for it. |
| The root cause turns out to be a stale artifact (not XML fragmentation) | Low | The local verification step above rules this in or out before any CI change is made. If stale artifact: close issue; the gap self-corrected on the next CI run after tests were added. |
| `Anela.Heblo.Adapters.Plaud.Tests` has no `coverlet.collector` reference, meaning it generates no XML at all | Informational | This is not a problem for this fix; that project has no application source references either. Note: add `coverlet.collector` to Plaud.Tests.csproj if coverage of the Plaud adapter is ever desired. |

## Specification Amendments

1. **FR-1 should be rephrased**: the local repro command should run `dotnet test Anela.Heblo.sln` (solution-level), not just the three test assemblies, to reproduce the same multi-XML output that CI produces. Running only `Anela.Heblo.Tests` will show correct coverage and not reproduce the problem.

2. **FR-2 root cause ordering**: the spec lists "filter pattern excludes test projects" as the most likely cause. Based on codebase inspection, the actual most-likely cause is XML fragmentation without merging. The CI command has no `--include`/`--exclude` flags that would exclude a test project, and all test projects have `coverlet.collector` installed (Plaud.Tests excepted, but it has no application references). Reorder the investigation priorities accordingly.

3. **FR-3 acceptance criteria — add**: the merged Cobertura XML must be present in the `coverage-backend` artifact and the path `coverage/merged/Cobertura.xml` must contain a `line-rate` ≥ 0.60 for `ScanPackingOrderHandler.cs` after the path-rewrite transformation.

4. **Out of scope — clarify**: the spec says "writing new unit tests" is out of scope. This is correct. The three test files confirmed present in the codebase (`ScanPackingOrderHandlerTests`, `ScanPackingOrderPackerTests`, `ScanPackingOrderHandlerPackagePersistenceTests`) cover all the branches identified in the brief. No new tests are needed.

## Prerequisites

- No migrations, no infrastructure changes, no config changes required.
- ReportGenerator is a dotnet global tool; no additional secrets or permissions needed.
- Confirm the local repro command above before writing any CI YAML to avoid solving the wrong problem.
