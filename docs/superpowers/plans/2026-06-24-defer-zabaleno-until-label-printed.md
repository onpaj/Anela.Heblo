# Defer "Zabaleno" Transition Until Shipping Label Is Printed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop moving single-package packing orders to Shoptet state 52 "Zabaleno" at scan time; transition only after the carrier label is confirmed printed — matching the existing multi-package flow.

**Architecture:** `ScanPackingOrderHandler` currently auto-marks single-package orders as packed immediately after `CreateShipmentAsync` returns. But that call only confirms Shoptet *accepted* the shipment request; the carrier label is generated asynchronously and can fail (shipment → `request_failed`, filtered out of `GetLabelsByOrderCodeAsync`). The fix returns `PendingCompletion = true` for all newly created shipments and removes the scan-time mark. The frontend already drives `MarkAsPacked` (via `…/packing/complete`) only after a label is confirmed fetched & printed — multi-package through `PackingLabelPrintModal`, single-package through `PackingLabelPrinter`'s existing `pendingCompletion`-gated completion effect. A small routing tweak keeps single-package on the inline printer.

**Tech Stack:** .NET 8 (MediatR handler, xUnit + Moq + FluentAssertions), React + TypeScript (react-scripts test / Jest + Testing Library).

**Out of scope (confirmed with user):** the expedition/picking flow (`PrintExpeditionOrderHandler` → state 26 "Balí se"), which does not request shipping labels.

---

## File Structure

- **Modify** `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — drop the scan-time mark for new shipments; always defer via `PendingCompletion = true`.
- **Modify** `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — invert the single-package "marks as packed" test.
- **Modify** `frontend/src/components/baleni/PackingShipmentCreator.tsx` — route the multi-package modal by package count so single-package keeps the inline printer.
- **Modify** `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx` — reflect `pendingCompletion: true` on new single-package shipments.
- **Modify** `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx` — add coverage that a single-package `pendingCompletion` shipment completes after printing.

Unchanged on purpose: the existing-shipment reprint path (`ScanPackingOrderHandler.cs:111`) still calls `TryMarkAsPackedAsync` — that branch is only reached when `existingLabels.Count > 0`, so a usable label demonstrably exists. `PackingLabelPrinter.tsx` needs no code change; its completion effect is already gated on `shipment.pendingCompletion === true`.

---

## Task 1: Backend — defer the state transition for new shipments

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs:380-411`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs:187-199`

- [ ] **Step 1: Rewrite the single-package test to expect deferral (failing test)**

Replace the existing `Handle_NewShipmentCreated_MarksOrderAsPacked` test (lines 380-411) with the inverted assertion:

```csharp
    // New single-package shipment: the "Zabaleno" transition is DEFERRED to the FE
    // (PendingCompletion = true) and NOT marked at scan time — the carrier label is
    // generated asynchronously and may not exist yet.
    [Fact]
    public async Task Handle_NewSinglePackageShipment_DefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }]);

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.PendingCompletion.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Leave `Handle_LabelsExist_MarksOrderAsPacked`, `Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse` (both exercise the existing-shipment path), and `Handle_MultiPackage_..._DefersMarkAsPacked` unchanged.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests.Handle_NewSinglePackageShipment_DefersMarkAsPacked"`
Expected: FAIL — current code calls `MarkAsPackedAsync` once, so `Times.Never` is violated.

- [ ] **Step 3: Remove the scan-time mark and always defer**

In `ScanPackingOrderHandler.cs`, replace lines 187-199:

```csharp
        var pendingCompletion = n >= 2;
        if (!pendingCompletion)
        {
            await TryMarkAsPackedAsync(request.OrderCode, ct);
        }

        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = pendingCompletion,
        });
```

with:

```csharp
        // The Shoptet "Zabaleno" (52) transition is deferred to the FE, which calls
        // .../packing/complete only after every carrier label is confirmed fetched & printed.
        // CreateShipmentAsync succeeding means Shoptet accepted the request, NOT that a usable
        // label was produced (labels generate asynchronously and can fail). Marking here would
        // move the order to "Zabaleno" even when no label exists. Single- and multi-package
        // orders share this deferred path.
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = true,
        });
```

(`n` is still used above for weight math; only the `pendingCompletion` variable and the mark call are removed. `TryMarkAsPackedAsync` remains — still called by the existing-shipment path at line 111.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"`
Expected: PASS (all tests in the class, including the multi-package and existing-shipment cases).

- [ ] **Step 5: Build and format**

Run: `cd backend && dotnet build && dotnet format`
Expected: build succeeds; format reports no remaining changes.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs
git commit -m "fix: defer Zabaleno transition for single-package orders until label is printed"
```

---

## Task 2: Frontend — keep single-package on the inline printer

**Files:**
- Modify: `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx:54-62,97-100`
- Modify: `frontend/src/components/baleni/PackingShipmentCreator.tsx:86-100`

- [ ] **Step 1: Update the fixture and assert single-package routing (failing/guard test)**

In `PackingShipmentCreator.test.tsx`, update the `newShipment` fixture (lines 54-62) to carry the now-real flag, and add an explicit assertion that the modal does NOT render for it:

```ts
const newShipment: ScanShipment = {
  shipmentGuid: 'guid-new',
  packages: [{
    trackingNumber: null,
    labelUrl: 'https://carrier.example.com/new.pdf',
    labelZpl: null,
  }],
  alreadyExisted: false,
  pendingCompletion: true,
};
```

Then extend the existing "shows PackingLabelPrinter immediately when shipment is new" test (lines 97-100) to also assert the modal is absent:

```tsx
  it('shows PackingLabelPrinter immediately when shipment is new', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={newShipment} />);
    expect(screen.getByTestId('label-printer')).toBeInTheDocument();
    expect(screen.queryByTestId('label-print-modal')).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test src/components/baleni/__tests__/PackingShipmentCreator.test.tsx --watchAll=false`
Expected: FAIL — with `pendingCompletion: true` on a single-package shipment, the current `if (shipmentForPrint.pendingCompletion)` routes to the modal, so `label-printer` is absent / `label-print-modal` is present.

- [ ] **Step 3: Route the modal by package count**

In `PackingShipmentCreator.tsx`, change the condition on line 87 from:

```tsx
  if (shipmentForPrint) {
    if (shipmentForPrint.pendingCompletion) {
      return (
        <PackingLabelPrintModal
```

to:

```tsx
  if (shipmentForPrint) {
    if (shipmentForPrint.pendingCompletion && shipmentForPrint.packages.length >= 2) {
      return (
        <PackingLabelPrintModal
```

This keeps multi-package new shipments on the modal (auto-polling readiness) and single-package new shipments on the inline `PackingLabelPrinter`. The printer's existing effect fires `useCompletePackingOrder` once the single label prints, because `shipment.pendingCompletion === true`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test src/components/baleni/__tests__/PackingShipmentCreator.test.tsx --watchAll=false`
Expected: PASS (single-package → printer; multi-package modal test still passes).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/PackingShipmentCreator.tsx frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx
git commit -m "fix: route packing modal by package count so single-package uses inline printer"
```

---

## Task 3: Frontend — cover single-package completion-after-print

**Files:**
- Modify: `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx:267-272`

- [ ] **Step 1: Add the single-package completion test**

In `PackingLabelPrinter.test.tsx`, immediately after the existing "does NOT fire completion for a single-package (pendingCompletion absent) shipment" test (lines 267-272, which models an existing-shipment reprint and stays as-is), add:

```tsx
  it('fires completion once for a single-package pendingCompletion shipment after the label prints', () => {
    const shipment = { ...makeShipment([pkg1]), pendingCompletion: true };
    render(<PackingLabelPrinter order={makeOrder('250001')} shipment={shipment} />);

    fireAck(0); // the only label acknowledged → done

    expect(mockComplete).toHaveBeenCalledTimes(1);
    expect(mockComplete).toHaveBeenCalledWith('250001', expect.objectContaining({ onError: expect.any(Function) }));
  });
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test src/components/baleni/__tests__/PackingLabelPrinter.test.tsx --watchAll=false`
Expected: PASS — `PackingLabelPrinter` already completes when `isDone && shipment.pendingCompletion`; this locks in the single-package case. The "absent" test must still pass (reprints don't re-fire completion).

- [ ] **Step 3: Build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build and lint succeed.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
git commit -m "test: cover single-package completion after label print"
```

---

## End-to-End Verification

After all tasks:

1. **Backend:** `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"` — all green.
2. **Frontend:** `cd frontend && npm run build && npm run lint && CI=true npx react-scripts test src/components/baleni --watchAll=false` — all green.
3. **Behavioral checks** (manual or E2E reasoning on the Balení screen):
   - Single-package, label generates: scan → label prints → order becomes "Zabaleno" (52). ✓
   - Single-package, carrier never produces a label (persistent 404 on `…/label.pdf`): scan → print error, packer retries → order stays in its pre-pack state, NOT "Zabaleno". ✓ (the bug fixed)
   - Multi-package: unchanged — completes only after all labels print.
   - Existing-shipment reprint: still marks "Zabaleno" (label already exists). ✓
4. **Optional staging E2E:** `./scripts/run-playwright-tests.sh` for the packing module.
```
