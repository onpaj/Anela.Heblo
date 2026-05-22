# Direct CDN Label Print Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Print shipping label PDFs directly from the carrier CDN URL — eliminate the backend proxy endpoint that's failing with 401 because the frontend `fetch()` doesn't attach auth.

**Architecture:** Surface the carrier's `LabelUrl` (already known on the backend at `ShipmentLabelDto.LabelUrl`) through the scan/reset response DTOs to the frontend. The browser then loads the PDF directly from the CDN — first attempting a `fetch+blob+iframe` silent print, falling back to `window.open` if CORS blocks it. The `/api/packaging/orders/{orderCode}/label/pdf` proxy endpoint and its sibling `/api/shipment-labels/pdf` (plus the entire `GetShipmentLabelPdf` use case) are deleted as dead code.

**Tech Stack:** .NET 8 (MediatR vertical slice), xUnit + FluentAssertions + Moq for BE tests, React 18 + TypeScript, Jest + Testing Library for FE tests, NSwag-generated OpenAPI client.

---

## File Structure

### Backend — modify

| File | Responsibility | Change |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs` | Scan response DTO | Add `LabelUrl` + `LabelZpl` to `ScanShipmentPackage` |
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` | Scan orchestration | Populate `LabelUrl`/`LabelZpl` from `existingLabels` and `newLabels` |
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentResponse.cs` | Reset response DTO | Add `LabelUrl` + `LabelZpl` to `ResetShipmentPackage` |
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs` | Reset orchestration | Populate `LabelUrl`/`LabelZpl` from `newLabels` |
| `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` | Scan handler tests | Update label fixtures + assert new fields |
| `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs` | Reset handler tests | Update label fixtures + assert new fields |

### Backend — delete

| File | Why |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfHandler.cs` | No callers after controller endpoints removed |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfRequest.cs` | Same |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfResponse.cs` | Same |
| `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs` | Tests for deleted handler |

### Backend — modify (remove endpoints + dead translations)

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` | Delete `GetLabelPdf` action + unused `GetShipmentLabelPdf` using |
| `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs` | Delete `GetLabelPdf` action + unused `GetShipmentLabelPdf` using |
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Remove `ShipmentLabelPdfNotFound = 2904` (no remaining producers) |
| `frontend/src/i18n.ts` | Remove `ShipmentLabelPdfNotFound` translation entry |

### Frontend — modify

| File | Change |
|---|---|
| `frontend/src/api/hooks/useScanPackingOrder.ts` | Add `labelUrl: string \| null` + `labelZpl: string \| null` to `ScanShipmentPackage` |
| `frontend/src/api/hooks/useResetOrderShipment.ts` | (Imports `ScanShipment` from the above — no shape change needed there but verify) |
| `frontend/src/components/baleni/PackingShipmentCreator.tsx` | Pass `labelUrl` + `labelZpl` through `toLabels()` |
| `frontend/src/components/baleni/printLabelPdf.ts` | Rewrite: consume `label.labelUrl` directly; try CORS-fetch + blob iframe → fall back to `window.open` |
| `frontend/src/components/baleni/__tests__/printLabelPdf.test.ts` | Rewrite for new contract (no `/api/...` URL, label has `labelUrl`) |
| `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx` | Update fixtures to include `labelUrl: null` |
| `frontend/src/api/generated/api-client.ts` | Regenerated automatically by `npm run generate-client` after BE changes |

---

## Pre-flight: Worktree & branch

This branch (`feature/conductor-skip-be-startup`) is the active Conductor workspace and should be used as-is. The user is the solo developer — no separate branch needs to be cut.

**Build/test/lint commands you will run repeatedly:**

```bash
# Backend
dotnet build backend/Anela.Heblo.sln
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Packaging|FullyQualifiedName~ShipmentLabels" --no-build
dotnet format backend/Anela.Heblo.sln

# Frontend
cd frontend && npm run build    # also regenerates api-client.ts via prebuild
cd frontend && npm run lint
cd frontend && npm test -- baleni --watchAll=false
```

---

## Task 1: Add LabelUrl/LabelZpl to ScanShipmentPackage DTO (failing test first)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`

- [ ] **Step 1: Update `Handle_LabelsExist_ReturnsExistingShipmentWithAlreadyExistedTrue` to assert `LabelUrl`/`LabelZpl` propagation**

The existing test (lines 96-130) already creates a label with `LabelUrl = "https://example.com/label.pdf"`. We extend it to set `LabelZpl` and assert both are surfaced on the returned `ScanShipmentPackage`.

Replace lines 100-106 (the `existingLabel` setup) with:

```csharp
var existingLabel = new ShipmentLabel
{
    ShipmentGuid = shipmentGuid,
    OrderCode = "0001234",
    PackageName = "P1",
    LabelUrl = "https://example.com/label.pdf",
    LabelZpl = "^XA...^XZ",
};
```

Then append before the closing `}` of the test (after line 129):

```csharp
        response.Shipment.Packages[0].Name.Should().Be("P1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA...^XZ");
```

(Remove the existing `Should().HaveCount(1)` if it was the last assertion — replace with the three above.)

- [ ] **Step 2: Update `Handle_NoExistingShipment_CreatesNewShipmentWithAlreadyExistedFalse` to assert label URL propagation on newly created shipment**

In the `SetupSequence` (lines 214-217), replace the second `ReturnsAsync` so the new label carries a URL:

```csharp
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel
            {
                ShipmentGuid = shipmentGuid,
                OrderCode = "0001234",
                PackageName = "P1",
                LabelUrl = "https://carrier.example.com/new-label.pdf",
            }]);
```

Then append before the closing `}` of the test (after line 239):

```csharp
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().BeNull();
```

- [ ] **Step 3: Run the two tests — they must fail (compile error, `LabelUrl`/`LabelZpl` don't exist on `ScanShipmentPackage`)**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~ScanPackingOrderHandlerTests.Handle_LabelsExist|FullyQualifiedName~ScanPackingOrderHandlerTests.Handle_NoExistingShipment_Creates" \
  --no-restore
```

Expected: build failure with `'ScanShipmentPackage' does not contain a definition for 'LabelUrl'` and `... 'LabelZpl'`.

- [ ] **Step 4: Add `LabelUrl` and `LabelZpl` to `ScanShipmentPackage`**

Edit `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`, replacing lines 53-56:

```csharp
public class ScanShipmentPackage
{
    public string Name { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
}
```

- [ ] **Step 5: Populate the new fields in `ScanPackingOrderHandler`**

Edit `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`.

Replace lines 66-68 (existing-shipment branch):

```csharp
                Packages = existingLabels
                    .Select(l => new ScanShipmentPackage
                    {
                        Name = l.PackageName,
                        LabelUrl = l.LabelUrl,
                        LabelZpl = l.LabelZpl,
                    })
                    .ToList(),
```

Replace lines 108-112 (new-shipment branch — drop the stale comment about the proxy):

```csharp
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ScanShipmentPackage
            {
                Name = l.PackageName,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
            }).ToList()
            : [new ScanShipmentPackage { Name = "PKG-1" }];
```

- [ ] **Step 6: Run the two updated tests — they must pass**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~ScanPackingOrderHandlerTests.Handle_LabelsExist|FullyQualifiedName~ScanPackingOrderHandlerTests.Handle_NoExistingShipment_Creates" \
  --no-restore
```

Expected: PASS.

- [ ] **Step 7: Run the full `ScanPackingOrderHandlerTests` suite to verify no regression**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ScanPackingOrderHandlerTests" --no-restore
```

Expected: all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs
git commit -m "feat(packaging): surface labelUrl/labelZpl on scan response packages"
```

---

## Task 2: Add LabelUrl/LabelZpl to ResetShipmentPackage DTO (mirror)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`

- [ ] **Step 1: Update `MakeLabel` helper to accept a `labelUrl` argument with a sensible default**

Edit `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`. Replace lines 45-52:

```csharp
    private static ShipmentLabel MakeLabel(
        Guid shipmentGuid,
        string packageName = "P1",
        string? labelUrl = "https://example.com/label.pdf",
        string? labelZpl = null) =>
        new()
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = packageName,
            LabelUrl = labelUrl,
            LabelZpl = labelZpl,
        };
```

- [ ] **Step 2: Update `Handle_HappyPath_DeletesOldAndCreatesNewShipment` to assert new fields propagate**

In the same file, replace the `SetupSequence` block at lines 123-126 to supply a custom new label:

```csharp
        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", "^XA-NEW^XZ")]);
```

Then append before the closing `}` of the test (after line 143):

```csharp
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA-NEW^XZ");
```

- [ ] **Step 3: Run the test — it must fail (compile error)**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests.Handle_HappyPath" --no-restore
```

Expected: build failure: `'ResetShipmentPackage' does not contain a definition for 'LabelUrl'`.

- [ ] **Step 4: Add `LabelUrl`/`LabelZpl` to `ResetShipmentPackage`**

Edit `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentResponse.cs`, replacing lines 23-26:

```csharp
public class ResetShipmentPackage
{
    public string Name { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
}
```

- [ ] **Step 5: Populate the new fields in `ResetOrderShipmentHandler`**

Edit `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`. Replace lines 86-89:

```csharp
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ResetShipmentPackage
            {
                Name = l.PackageName,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
            }).ToList()
            : [new ResetShipmentPackage { Name = "PKG-1" }];
```

- [ ] **Step 6: Run the test — it must pass**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests.Handle_HappyPath" --no-restore
```

Expected: PASS.

- [ ] **Step 7: Run the full Reset handler suite**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests" --no-restore
```

Expected: all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs
git commit -m "feat(packaging): surface labelUrl/labelZpl on reset response packages"
```

---

## Task 3: Remove the `GetLabelPdf` proxy endpoint from `PackagingController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Delete the endpoint, its summary comment, and the now-unused `using`**

Edit `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`.

Remove line 3 (`using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;`).

Remove lines 48-73 (the `[HttpGet("orders/{orderCode}/label/pdf")]` action and its XML doc comment).

The file should end at the `ResetShipment` action (around what was line 46) plus the closing braces.

- [ ] **Step 2: Build the API project — expect it to succeed**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore
```

Expected: build succeeds. (The `using` for `ErrorCodes` and `GetShipmentLabelPdf` is no longer needed inside this controller; `ErrorCodes` only appears in the deleted action.)

If you see `error CS0246: ErrorCodes`, remove the `using Anela.Heblo.Application.Shared;` import as well.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "refactor(packaging): drop /label/pdf proxy endpoint"
```

---

## Task 4: Remove the `GetLabelPdf` proxy endpoint from `ShipmentLabelsController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`

- [ ] **Step 1: Delete the `pdf` endpoint, its summary comment, and the unused `using`**

Edit `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`.

Remove line 3 (`using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;`).

Remove lines 57-83 (the `[HttpGet("pdf")]` action and its XML doc comment).

If the `using Anela.Heblo.Application.Shared;` (for `ErrorCodes`) is then unused, remove it. Verify the remaining file: the controller should now expose only `GetLabels` (`[HttpPost]`) and `CreateShipment` (`[HttpPost("create")]`).

- [ ] **Step 2: Build the API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore
```

Expected: build succeeds. (Both endpoint deletions are now in; the `GetShipmentLabelPdf` use case is still defined but has zero callers — that's deleted in Task 5.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs
git commit -m "refactor(shipment-labels): drop /pdf proxy endpoint"
```

---

## Task 5: Delete the `GetShipmentLabelPdf` use case folder and its tests

**Files (all deleted):**
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfResponse.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs`

- [ ] **Step 1: Confirm no remaining references before deletion**

Run from repo root:

```bash
grep -rn "GetShipmentLabelPdf" backend/src backend/test 2>/dev/null
```

Expected output: only matches inside the four files listed above (the use-case folder + its test file). If any other file references `GetShipmentLabelPdf*`, stop and surface to the user.

- [ ] **Step 2: Delete the use-case folder**

```bash
rm -r backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf
```

- [ ] **Step 3: Delete the test file**

```bash
rm backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs
```

- [ ] **Step 4: Build the whole solution**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests PASS, count drops by the 6 deleted `GetShipmentLabelPdfHandlerTests` cases.

- [ ] **Step 6: Commit**

```bash
git add -A backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases \
        backend/test/Anela.Heblo.Tests/Application/ShipmentLabels
git commit -m "refactor(shipment-labels): remove GetShipmentLabelPdf use case (dead code)"
```

---

## Task 6: Remove `ShipmentLabelPdfNotFound` error code and FE translation

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `frontend/src/i18n.ts`

After Tasks 3-5, `ShipmentLabelPdfNotFound` has no producers and no consumers — it's dead surface area.

- [ ] **Step 1: Verify dead-code claim**

```bash
grep -rn "ShipmentLabelPdfNotFound" backend frontend/src 2>/dev/null
```

Expected: matches only in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` and `frontend/src/i18n.ts` (and the regenerated `frontend/src/api/generated/api-client.ts`, which will be refreshed in Task 7).

If anything else references it, stop and surface to the user.

- [ ] **Step 2: Remove the enum value from `ErrorCodes`**

Edit `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Delete lines 326-327:

```csharp
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShipmentLabelPdfNotFound = 2904,
```

(Keep the numeric gap — do not renumber other codes.)

- [ ] **Step 3: Remove the FE translation entry**

Edit `frontend/src/i18n.ts`. Delete line 262:

```typescript
        ShipmentLabelPdfNotFound: "PDF štítek zásilky nebyl nalezen.",
```

- [ ] **Step 4: Build backend**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs frontend/src/i18n.ts
git commit -m "refactor: drop dead ShipmentLabelPdfNotFound error code"
```

---

## Task 7: Regenerate the frontend OpenAPI client

**Files:**
- Auto-regenerated: `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Regenerate the TypeScript client**

```bash
cd frontend && npm run generate-client
```

Expected: completes without errors. NSwag re-emits `api-client.ts` from the latest swagger.

- [ ] **Step 2: Confirm the generated `ScanShipmentPackage` / `ResetShipmentPackage` now carry `labelUrl` / `labelZpl`**

```bash
grep -nE "class (ScanShipmentPackage|ResetShipmentPackage)" frontend/src/api/generated/api-client.ts
```

Then read 25 lines starting at each match and verify the body declares `labelUrl?: string | undefined;` and `labelZpl?: string | undefined;`.

- [ ] **Step 3: Confirm the deleted endpoints are gone from the client**

```bash
grep -n "shipment-labels/pdf\|orders/.*label/pdf\|ShipmentLabelPdfNotFound" frontend/src/api/generated/api-client.ts
```

Expected: zero matches.

- [ ] **Step 4: Type-check the frontend (full build)**

```bash
cd frontend && npm run build
```

Expected: build succeeds. If TS errors mention `ScanShipmentPackage` shape mismatches inside hooks, that's the trigger for Task 8.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(frontend): regenerate api client"
```

---

## Task 8: Extend FE `ScanShipmentPackage` interface in hooks

**Files:**
- Modify: `frontend/src/api/hooks/useScanPackingOrder.ts`

`useResetOrderShipment.ts` imports `ScanShipment` from `useScanPackingOrder`, so the change cascades.

- [ ] **Step 1: Extend the `ScanShipmentPackage` interface**

Edit `frontend/src/api/hooks/useScanPackingOrder.ts`. Replace lines 37-39:

```typescript
export interface ScanShipmentPackage {
  name: string;
  labelUrl: string | null;
  labelZpl: string | null;
}
```

- [ ] **Step 2: Type-check + build**

```bash
cd frontend && npm run build
```

Expected: PASS. (No other file currently constructs `ScanShipmentPackage` literally — they come from the API response.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useScanPackingOrder.ts
git commit -m "feat(frontend): add labelUrl/labelZpl to ScanShipmentPackage"
```

---

## Task 9: Wire `labelUrl`/`labelZpl` through `PackingShipmentCreator.toLabels()`

**Files:**
- Modify: `frontend/src/components/baleni/PackingShipmentCreator.tsx`
- Modify: `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`

- [ ] **Step 1: Add `labelUrl` to the test fixtures so the suite still type-checks**

Edit `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`. Replace lines 25-35:

```typescript
const newShipment: ScanShipment = {
  shipmentGuid: 'guid-new',
  packages: [{ name: 'PKG-1', labelUrl: 'https://carrier.example.com/new.pdf', labelZpl: null }],
  alreadyExisted: false,
};

const existingShipment: ScanShipment = {
  shipmentGuid: 'guid-existing',
  packages: [{ name: 'PKG-1', labelUrl: 'https://carrier.example.com/existing.pdf', labelZpl: null }],
  alreadyExisted: true,
};
```

- [ ] **Step 2: Pass `labelUrl` / `labelZpl` through `toLabels()`**

Edit `frontend/src/components/baleni/PackingShipmentCreator.tsx`. Replace lines 13-21:

```typescript
function toLabels(shipment: ScanShipment): ShipmentLabelDto[] {
  return shipment.packages.map(
    (pkg) =>
      ({
        shipmentGuid: shipment.shipmentGuid,
        packageName: pkg.name,
        labelUrl: pkg.labelUrl ?? undefined,
        labelZpl: pkg.labelZpl ?? undefined,
      }) as ShipmentLabelDto
  );
}
```

(The cast to `ShipmentLabelDto` stays — the generated DTO has `labelUrl?: string | undefined` so we map `null → undefined`.)

- [ ] **Step 3: Run the `PackingShipmentCreator` test suite**

```bash
cd frontend && npm test -- PackingShipmentCreator --watchAll=false
```

Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/baleni/PackingShipmentCreator.tsx \
        frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx
git commit -m "feat(packing): pass labelUrl through toLabels"
```

---

## Task 10: Rewrite `printLabelPdf` test (RED first)

**Files:**
- Rewrite: `frontend/src/components/baleni/__tests__/printLabelPdf.test.ts`

The existing tests assert the old `/api/shipment-labels/pdf` URL and pass `getAuthenticatedApiClient` through — both go away. We rewrite the entire suite for the new contract:

- Happy path: CORS fetch returns OK PDF → blob iframe is mounted, `print()` is called, iframe is removed, blob URL revoked.
- CORS-blocked path: `fetch()` rejects with `TypeError` → falls back to `window.open(labelUrl, '_blank', 'noopener,noreferrer')`, no iframe created.
- Non-OK path: `fetch()` resolves with `ok: false` → falls back to `window.open`.
- No `labelUrl`: function is a no-op (does not throw, does not call `fetch`, does not call `window.open`).

- [ ] **Step 1: Replace the entire test file with the new suite**

Overwrite `frontend/src/components/baleni/__tests__/printLabelPdf.test.ts`:

```typescript
import { printLabelPdf } from '../printLabelPdf';
import type { ShipmentLabelDto } from '../../../api/generated/api-client';

const flushAsync = () => new Promise(resolve => setTimeout(resolve, 0));

const labelWithUrl: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
  packageName: 'Zásilka 1',
  labelUrl: 'https://carrier.example.com/label.pdf',
} as ShipmentLabelDto;

const labelWithoutUrl: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
  packageName: 'Zásilka 1',
} as ShipmentLabelDto;

let originalOpen: typeof window.open;

beforeEach(() => {
  jest.clearAllMocks();
  URL.createObjectURL = jest.fn().mockReturnValue('blob:test-url');
  URL.revokeObjectURL = jest.fn();
  originalOpen = window.open;
  window.open = jest.fn() as unknown as typeof window.open;
});

afterEach(() => {
  window.open = originalOpen;
});

describe('printLabelPdf', () => {
  it('fetches the carrier CDN URL directly (no /api/... proxy)', async () => {
    global.fetch = jest.fn().mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(global.fetch).toHaveBeenCalledWith('https://carrier.example.com/label.pdf');
    expect(window.open).not.toHaveBeenCalled();

    jest.restoreAllMocks();
  });

  it('mounts a hidden iframe with the blob URL, calls print, removes iframe, revokes blob URL', async () => {
    global.fetch = jest.fn().mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);
    const removeChildSpy = jest.spyOn(document.body, 'removeChild').mockImplementation(() => null as any);

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    expect(iframe.style.display).toBe('none');
    expect(iframe.src).toBe('blob:test-url');
    expect(appendChildSpy).toHaveBeenCalledWith(iframe);

    const printMock = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock },
      configurable: true,
    });
    iframe.onload!(new Event('load'));

    expect(printMock).toHaveBeenCalledTimes(1);
    expect(removeChildSpy).toHaveBeenCalledWith(iframe);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');

    jest.restoreAllMocks();
  });

  it('falls back to window.open when fetch throws (CORS)', async () => {
    global.fetch = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      'https://carrier.example.com/label.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('falls back to window.open when fetch returns non-ok', async () => {
    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 404 });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      'https://carrier.example.com/label.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('is a no-op when labelUrl is missing', async () => {
    global.fetch = jest.fn();
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithoutUrl);
    await flushAsync();

    expect(global.fetch).not.toHaveBeenCalled();
    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();

    appendChildSpy.mockRestore();
  });
});
```

- [ ] **Step 2: Run the new test suite — it MUST fail (the production code still calls `/api/...`)**

```bash
cd frontend && npm test -- printLabelPdf --watchAll=false
```

Expected: at least the "fetches the carrier CDN URL directly" test fails. That's the RED we're targeting before rewriting `printLabelPdf.ts`.

---

## Task 11: Rewrite `printLabelPdf.ts` (GREEN)

**Files:**
- Rewrite: `frontend/src/components/baleni/printLabelPdf.ts`

- [ ] **Step 1: Overwrite with the new implementation**

Replace the entire contents of `frontend/src/components/baleni/printLabelPdf.ts`:

```typescript
import { ShipmentLabelDto } from '../../api/generated/api-client';

const openInNewTab = (url: string): void => {
  window.open(url, '_blank', 'noopener,noreferrer');
};

const silentPrintViaBlob = async (url: string): Promise<boolean> => {
  let response: Response;
  try {
    response = await fetch(url);
  } catch {
    return false;
  }
  if (!response.ok) return false;

  const blob = await response.blob();
  const blobUrl = URL.createObjectURL(blob);
  const iframe = document.createElement('iframe');
  iframe.style.display = 'none';
  iframe.src = blobUrl;
  iframe.onload = () => {
    iframe.contentWindow?.print();
    document.body.removeChild(iframe);
    URL.revokeObjectURL(blobUrl);
  };
  document.body.appendChild(iframe);
  return true;
};

export const printLabelPdf = (_orderCode: string, label: ShipmentLabelDto): void => {
  const labelUrl = label.labelUrl;
  if (!labelUrl) return;

  void silentPrintViaBlob(labelUrl).then((printed) => {
    if (!printed) openInNewTab(labelUrl);
  });
};
```

Notes:
- `_orderCode` is kept in the signature so the call sites in `PackingLabelPrinter.tsx` don't need to change.
- We only fall back to `window.open` when fetch fails *or* response is non-OK — the silent path always wins when the CDN allows CORS.
- No throws: a missing `labelUrl` is a silent no-op (matches the existing "swallow on failure" UX).

- [ ] **Step 2: Run the `printLabelPdf` test suite — it must now pass**

```bash
cd frontend && npm test -- printLabelPdf --watchAll=false
```

Expected: all 5 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/printLabelPdf.ts \
        frontend/src/components/baleni/__tests__/printLabelPdf.test.ts
git commit -m "feat(packing): print label PDF directly from carrier CDN URL"
```

---

## Task 12: Verify `PackingLabelPrinter` and the rest of the `baleni` suite

**Files:**
- Inspect (no expected change): `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx`

`PackingLabelPrinter` constructs `labels: ShipmentLabelDto[]` from props passed by `PackingShipmentCreator`. The generated `ShipmentLabelDto` already has `labelUrl` as optional, so this should compile unchanged. Verify with the suite.

- [ ] **Step 1: Run the full `baleni` test suite**

```bash
cd frontend && npm test -- baleni --watchAll=false
```

Expected: all tests PASS. If `PackingLabelPrinter.test.tsx` fails because a fixture lacks `labelUrl`, add `labelUrl: 'https://carrier.example.com/x.pdf'` to those fixtures — but make no other code changes.

- [ ] **Step 2: If fixtures were changed, commit. Otherwise skip.**

```bash
git status --short frontend/src/components/baleni/__tests__/
# if changes:
git add frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
git commit -m "test(packing): update PackingLabelPrinter fixtures for labelUrl"
```

---

## Task 13: Full verification gate

- [ ] **Step 1: Format backend**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no errors. If anything was reformatted, commit:

```bash
git add -A backend
git commit -m "chore: dotnet format"
```

- [ ] **Step 2: Build backend**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: 0 errors, 0 warnings introduced by this change.

- [ ] **Step 3: Run all backend tests**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests PASS. Count down by ~6 tests vs main (`GetShipmentLabelPdfHandlerTests` removed).

- [ ] **Step 4: Frontend lint + build + tests**

```bash
cd frontend && npm run lint && npm run build && npm test -- --watchAll=false
```

Expected: all green.

- [ ] **Step 5: Dead-code cleanup grep**

```bash
grep -rn "GetShipmentLabelPdf\|/label/pdf\|shipment-labels/pdf\|ShipmentLabelPdfNotFound" \
  backend/src backend/test frontend/src 2>/dev/null
```

Expected output: empty. (The only acceptable matches would be in `frontend/src/api/generated/api-client.ts` if NSwag still leaks something — investigate if so.)

- [ ] **Step 6: Manual smoke check against staging (only the dev driving this can run it)**

   1. Open the Baleni kiosk page in Chrome → DevTools → Network tab.
   2. Scan a real packing order against staging.
   3. **Assert:** no request to `/api/packaging/orders/.../label/pdf` and no request to `/api/shipment-labels/pdf`.
   4. **Assert:** a `GET` request goes directly to a carrier CDN host (Shoptet/Balikobot domain) with status 200.
   5. **Assert (happy path / CORS allowed):** the browser's print dialog opens automatically.
   6. **Assert (fallback path / CORS blocked):** a new tab opens containing the PDF; the operator presses Ctrl+P.

Report the outcome (which branch fired) to the user — that determines whether the silent-print UX is preserved on staging or whether we land on the `window.open` fallback as the new normal.

---

## Self-Review (run before handing off)

- **Spec coverage:** Plan covers (1) propagating `labelUrl`/`labelZpl` through scan + reset DTOs, (2) FE rewrite of `printLabelPdf` with CORS fallback, (3) deletion of the proxy endpoint(s) + handler folder + tests, (4) regeneration of the FE client, (5) verification incl. manual kiosk smoke. The spec's open question about the `ShipmentLabels` folder is honored — only the `GetShipmentLabelPdf` subfolder is deleted; the rest stays.
- **Placeholder scan:** Every step contains either exact code or exact commands. No "TBD", no "handle edge cases", no naked "write tests".
- **Type consistency:** `LabelUrl` / `LabelZpl` are the names used everywhere on the BE; `labelUrl` / `labelZpl` (camelCase) on the FE. `ScanShipmentPackage` / `ResetShipmentPackage` names match across Task 1, 2, 7, 8, 9.

---

## Execution Handoff

Plan saved to `docs/superpowers/plans/2026-05-21-direct-cdn-label-print.md`.

You invoked `/subagent-driven-development` alongside `/writing-plans` — the next step is to dispatch a fresh subagent per task with two-stage review between tasks.
