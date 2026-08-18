# Implementation: add-null-detail-guard-test

## What was implemented
Added FR-5 test coverage to `ShoptetApiInvoiceSourceTests`: a new `[Fact]`
`GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch` that
verifies the list-mode path's null-detail guard in `ShoptetApiInvoiceSource.GetAllAsync`
(`if (detail != null) detailDtos.Add(detail);`). The test arranges two invoice
codes ("A", "B") in the list response, has `GetInvoiceAsync("A", ...)` return
null (simulating a detail fetch miss) and `GetInvoiceAsync("B", ...)` return a
valid DTO, then asserts: the call does not throw, the returned batch contains
exactly one invoice ("B"), and both codes were still passed to `GetInvoiceAsync`
(proving the loop does not short-circuit/abort on the null result). The file
was replaced in full with the exact verbatim content given in the task-context
file — the four pre-existing test methods (FR-1, FR-2, FR-3, FR-4 theory) are
byte-for-byte unchanged; only the new FR-5 method was appended.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — added the FR-5 `[Fact]` method after the FR-4 `[Theory]`; no other changes.

## Tests
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`
  covers `ShoptetApiInvoiceSource.GetAllAsync`: single-invoice mode found (FR-1),
  single-invoice mode not found (FR-2), list-mode currency filter exclusion (FR-3),
  list-mode currency filter case-insensitivity (FR-4, 2 InlineData cases), and
  now list-mode null-detail guard (FR-5, new).

## How to verify
```bash
cd backend
dotnet build-server shutdown
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 \
  dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests" -nodeReuse:false
```
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes
Plain `dotnet test` (no env vars / no `-nodeReuse:false`) hung indefinitely in
this sandbox — a pre-existing, already-documented gotcha
(`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`): the test
project transitively references `Anela.Heblo.API.csproj`, whose Debug-only
`GenerateAccessMatrix` `BeforeTargets="Build"` target shells out to
`dotnet run --project .../Anela.Heblo.AccessMatrixGen`; stale `nodeReuse:true`
MSBuild/VBCSCompiler server processes in this sandbox deadlock against that
nested `dotnet` invocation (confirmed via `ps`/`/proc/<pid>/stack`: entire
process tree parked in `futex_do_wait` with zero CPU growth and zero new
build-output files for 10+ minutes, while the host itself was ~98% idle).
Followed the documented fix: killed the stuck process tree, ran
`dotnet build-server shutdown`, then re-ran with
`DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1` and
`-nodeReuse:false`, which completed reliably (single test: <1 ms; full class:
20 ms). No source or project files were touched to work around this — only the
build invocation flags. No other deviations from the task-context spec; the
implementation under test (`ShoptetApiInvoiceSource.GetAllAsync`'s null guard)
was already correct and unmodified — this task added the missing regression
test.

## PR Summary
Adds the FR-5 test that closes the last coverage gap in
`ShoptetApiInvoiceSourceTests`: proving `ShoptetApiInvoiceSource.GetAllAsync`'s
list-mode detail-fetch loop tolerates a null `GetInvoiceAsync` result for one
code without throwing or aborting the batch, correctly excluding only the
affected invoice while still processing the rest.

No production code changed — this is a test-only addition, file replaced
verbatim per the task-context spec with the four existing FR-1..FR-4 tests
left untouched.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — added `GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch` FR-5 test

## Status
DONE
