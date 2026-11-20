# Material Container Label Reprint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reprint one existing material-container barcode label from the containers list (row button) and from a new handheld-terminal scan screen.

**Architecture:** One new MediatR use case `ReprintMaterialContainerLabel` behind `POST /api/material-containers/{code}/reprint`, reusing `MaterialContainerLabelZplBuilder`, `ILabelPrintingService` and the `PrinterMediaState` guard. Frontend adds a `useReprintMaterialContainerLabel` hook, a row button on `MaterialContainerList`, and a terminal screen `ReprintLabelScreen` at `/terminal/reprint-label`.

**Tech Stack:** .NET 8 / MediatR / xUnit+Moq+FluentAssertions; React + TanStack Query + react-scripts jest; NSwag-generated TS client.

**Spec:** `docs/superpowers/specs/2026-11-18-container-label-reprint-design.md`

> **Superseded 2026-11-19:** Discarded containers can be reprinted too. The `MaterialContainerDiscarded = 2810` error code, its translation and the refusal tests described below were dropped (see commit a8f22711b and the spec). Only an unknown code (2809) is refused.

## Global Constraints
- DTOs/requests/responses are classes, never records; `*Response` inherits `BaseResponse`.
- Permission: `Feature.Manufacture_MaterialContainers`, `AccessLevel.Write`.
- Media type for the guard: `LabelMediaType.MaterialContainer`.
- New error code: `MaterialContainerDiscarded = 2810`, `[HttpStatusCode(HttpStatusCode.Conflict)]`, Czech: `"Kontejner je vyřazený – štítek nelze dotisknout."`
- Unknown code reuses `UnknownMaterialContainerCode` (2809, existing translation).
- The generated client **throws `SwaggerException` on non-200** — FE reads `errorCode` from the parsed `error.response` body (see `LabelIdentificationScreen.tsx` `resolveIdentifyErrorMessage` for the pattern). `if (!res.success)` branches are dead code.
- FE gate: `CI=false npm run build` + `npm run lint`; tests via `npx react-scripts test --watchAll=false <path>`. BE: `dotnet build` + `dotnet format`.
- Process doc `docs/processes/flow-lots-and-material-containers.md` owns `MaterialContainersController.cs` → must be updated in the same PR, then `python3 scripts/process-docs/check.py index`.

## Review Focus
1. **Scan with surrounding whitespace / lowercase** — terminal wedge may deliver `" m00000010 "`; handler trims; lookup is as-stored (codes are uppercase `M` + digits, so FE uppercases before sending). Test in Task 1 (trim) and Task 4 (uppercase).
2. **Double scan while a print is in flight** — second scan must not be dropped silently nor fire two parallel prints; terminal ignores scans while `isPending` and flashes `warn`. Test in Task 4.
3. **Media-change sheet open, operator scans another code** — the new scan must not overwrite the pending code silently; scans are ignored (warn flash) while the sheet is open. Test in Task 4.
4. **Unassigned container** (printed but never assigned) — reprint must succeed (only Discarded is refused). Test in Task 1.
5. **Printer/CUPS failure** — `PrintZplAsync` throws → media state must NOT be recorded; FE shows the generic error, terminal flashes `err`. Test in Task 1.

---

### Task 1: Backend use case + endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ReprintMaterialContainerLabel/ReprintMaterialContainerLabelRequest.cs`, `…Response.cs`, `…Handler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (add 2810 after 2809)
- Modify: `backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ReprintMaterialContainerLabelHandlerTests.cs`

**Interfaces:**
- Produces: `ReprintMaterialContainerLabelRequest : IRequest<ReprintMaterialContainerLabelResponse>` { `string Code`, `bool MediaChangeConfirmed` }
- Produces: `ReprintMaterialContainerLabelResponse : BaseResponse` { `MaterialContainerDto? Container`, `bool RequiresMediaChangeConfirmation` } with the two standard ctors (see `PrintMaterialContainerLabelsResponse`).
- Produces: controller action `[HttpPost("{code}/reprint")] Reprint(string code, [FromBody] ReprintMaterialContainerLabelBody body, ct)` where `ReprintMaterialContainerLabelBody` is a class in the use-case folder with `bool MediaChangeConfirmed`. Generated TS method name will be `materialContainers_Reprint(code, body)`.

- [ ] **Step 1: Write failing tests** (mock pattern from `PrintMaterialContainerLabelsHandlerTests`; `GetByCodeAsync` on the repo mock):
  - `Handle_AssignedContainer_PrintsZplForThatCode_AndRecordsMedia` — ZPL passed to `PrintZplAsync` equals `MaterialContainerLabelZplBuilder.Build(new[]{"M00000010"})`; `media.SaveAsync` called once; `Success` true; `Container.Code == "M00000010"`; `repo.AddRangeAsync` never called.
  - `Handle_UnassignedContainer_Prints` — success, print called once.
  - `Handle_CodeWithWhitespace_IsTrimmedBeforeLookup` — request `" M00000010 "` → `GetByCodeAsync("M00000010", …)`.
  - `Handle_UnknownCode_ReturnsUnknownMaterialContainerCode_AndDoesNotPrint`
  - `Handle_DiscardedContainer_ReturnsMaterialContainerDiscarded_AndDoesNotPrint`
  - `Handle_MediaChangeRequired_NotConfirmed_ReturnsFlag_AndDoesNotPrint` — media state whose last type is `LotRound` (call `RecordPrint(LabelMediaType.LotRound, "x")` on a default); `RequiresMediaChangeConfirmation` true.
  - `Handle_MediaChangeRequired_Confirmed_Prints`
  - `Handle_PrinterThrows_DoesNotRecordMedia` — `PrintZplAsync` throws `InvalidOperationException`; assert exception propagates and `media.SaveAsync` never called.
- [ ] **Step 2:** `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false` → FAIL (types missing).
- [ ] **Step 3: Implement.** Handler ctor deps: `ILogger<>`, `IMaterialContainerRepository`, `ILabelPrintingService`, `ICurrentUserService`, `IPrinterMediaStateRepository`. Order: trim code → lookup (null → 2809 with param `Code`) → Discarded → 2810 → media guard → print → `RecordPrint` + `SaveAsync` → log → return with `CreateMaterialContainersHandler.MapToDto(container)`. Add the 2810 enum member, then the controller action. No validator (empty code is rejected by lookup → 2809); no module registration needed beyond MediatR scan.
- [ ] **Step 4:** `dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~ReprintMaterialContainerLabel|FullyQualifiedName~ErrorHandlingTests"` (after a build) → PASS.
- [ ] **Step 5:** `dotnet format` then commit `feat: reprint endpoint for material container labels`.

### Task 2: Regenerate client, hook, i18n

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts` (regenerated — keep only the hunks for the new endpoint/types/enum member; see gotcha on unrelated drift)
- Modify: `frontend/src/api/hooks/useMaterialContainers.ts`
- Modify: `frontend/src/i18n.ts` (add `MaterialContainerDiscarded` next to `UnknownMaterialContainerCode`)
- Create: `frontend/src/api/hooks/reprintErrors.ts`
- Test: `frontend/src/api/hooks/__tests__/reprintErrors.test.ts`

**Interfaces:**
- Consumes: `materialContainers_Reprint(code: string, body: ReprintMaterialContainerLabelBody)` from Task 1.
- Produces: `useReprintMaterialContainerLabel()` → `useMutation<ReprintMaterialContainerLabelResponse, unknown, { code: string; mediaChangeConfirmed?: boolean }>`; invalidates `QUERY_KEYS.materialContainers` on success.
- Produces: `resolveReprintErrorMessage(error: unknown): string` in `reprintErrors.ts` — parses `SwaggerException.response` JSON for `errorCode`/`params` and returns `handleApiError(...)`; anything else returns `handleApiError({ success: false, errorCode: ErrorCodes.InternalServerError })` (use whichever generic code `errorHandler` already treats as generic).

- [ ] **Step 1:** Regenerate via `dotnet msbuild -t:GenerateFrontendClientManual` (API project); `git diff --stat` the client and revert unrelated hunks.
- [ ] **Step 2: Failing tests** for `resolveReprintErrorMessage`: SwaggerException with body `{"errorCode":"MaterialContainerDiscarded"}` → `"Kontejner je vyřazený – štítek nelze dotisknout."`; body for `UnknownMaterialContainerCode` → `"Neznámý štítek – nejprve jej vygenerujte v aplikaci."`; body `"<html>"` → generic message (non-empty, not throwing); plain `Error` → generic message. Note: `errorCode` arrives as a string.
- [ ] **Step 3:** Run → FAIL. Implement hook + resolver + i18n. Run → PASS.
- [ ] **Step 4:** Commit `feat: reprint hook and error mapping`.

### Task 3: List page row button

**Files:**
- Modify: `frontend/src/components/pages/MaterialContainerList.tsx`
- Test: `frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx` (add `useReprintMaterialContainerLabel` to the hook mock setup)

**Interfaces:** Consumes `useReprintMaterialContainerLabel`, `resolveReprintErrorMessage` (Task 2), existing `PrinterMediaChangeDialog`.

- [ ] **Step 1: Failing tests:**
  - `shows reprint button per row and calls mutate with that code` — click button with `aria-label="Dotisknout štítek M00000010"` → `mutate({ code: "M00000010", mediaChangeConfirmed: false }, …)`.
  - `hides reprint button for Discarded rows` (status `"Discarded"`).
  - `opens media-change dialog when response requires confirmation and resends confirmed`.
  - `shows error text when reprint fails` — invoke `onError` with a SwaggerException for 2810 → message visible.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement.** New last column header `""` (actions); cell with a small icon button (`Printer` from lucide, `title="Dotisk"`), disabled while that row is pending. Reuse the page's `pendingConfirm` state and the single `PrinterMediaChangeDialog` (pass `isPending` = either mutation pending). Error shown as an inline red banner above the table, dismissed on next action. Keep the file under ~400 lines; if it grows past, extract `MaterialContainerRow.tsx`.
- [ ] **Step 4:** Run → PASS. Commit `feat: reprint button on material containers list`.

### Task 4: Terminal reprint screen

**Files:**
- Create: `frontend/src/components/terminal/reprint-label/ReprintLabelScreen.tsx`
- Modify: `frontend/src/components/terminal/TerminalHome.tsx` (tile), `frontend/src/App.tsx` (route `reprint-label` under `/terminal`)
- Test: `frontend/src/components/terminal/reprint-label/__tests__/ReprintLabelScreen.test.tsx`; update `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` if it asserts tile count.

**Interfaces:** Consumes `useScanScreen` (`flash(tone, code)`), `BottomSheet`, Task 2 hook + resolver.

Tile: `{ id: 'reprint-label', title: 'Dotisk štítku', description: 'Naskenujte štítek kontejneru a vytiskněte jeho kopii', href: '/terminal/reprint-label', icon: Printer, comingSoon: false }`.

Screen behaviour: heading "Dotisk štítku", hint "Naskenujte štítek kontejneru", list of the last 10 reprinted codes this session (newest first, with ✓/✗). On scan: trim + uppercase → if a print is pending or the sheet is open → `flash('warn', code)` and ignore; else mutate with `mediaChangeConfirmed = false`. Success → `flash('ok', code)`, add to list. `requiresMediaChangeConfirmation` → keep code as pending, open sheet "Vyměňte roli za štítky kontejnerů" with buttons "Role vyměněna, tisknout" / "Zrušit". Confirm → resend pending with `mediaChangeConfirmed = true` (never remembered for later scans). Error → `flash('err', code)` + message under the hint.

- [ ] **Step 1: Failing tests** (wrap in `ScanProvider`, mock the hook module; simulate scans by calling the registered handler or typing into the wedge as `TransportBoxCheck.test.tsx` does):
  - `scan reprints the scanned code` — `mutate({ code: "M00000010", mediaChangeConfirmed: false })` for scan `" m00000010 "`.
  - `success adds code to history`.
  - `media change response opens sheet; confirm resends with mediaChangeConfirmed true; next scan also sends true`.
  - `scan while pending is ignored` (mutate called once).
  - `scan while sheet open is ignored and pending code unchanged`.
  - `error shows resolved message` (2810 body).
  - TerminalHome: `renders Dotisk štítku tile linking to /terminal/reprint-label`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement screen, tile, route.
- [ ] **Step 4:** Run → PASS. Commit `feat: terminal screen for reprinting container labels`.

### Task 5: Process doc + full validation

**Files:** Modify `docs/processes/flow-lots-and-material-containers.md` — add a "Reprint" step (endpoint, Discarded refused, unknown → 2809, media guard applies, no row created), add the new handler + screen to the code map / `owns`, bump `verified_at`.

- [ ] **Step 1:** Edit doc; run `python3 scripts/process-docs/check.py index` → clean.
- [ ] **Step 2:** `dotnet build` + `dotnet format --verify-no-changes`; full `dotnet test` (--no-build, `-p:UseSharedCompilation=false` on the build) → PASS.
- [ ] **Step 3:** `cd frontend && CI=false npm run build && npm run lint` → PASS; `npx react-scripts test --watchAll=false` → PASS.
- [ ] **Step 4:** Commit `docs: process doc for container label reprint`.
