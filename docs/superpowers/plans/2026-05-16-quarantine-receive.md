# Quarantine → Received: Fix isReceivable Flag Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow transport boxes in `Quarantine` state to be received via the dedicated Receive workflow (barcode scan pages).

**Architecture:** The domain state machine already supports `Quarantine → Received` (registered in `TransportBox._transitions` and accepted by `Receive()`). The only gap is that `GetTransportBoxByCodeHandler` computes `isReceivable` using only `Reserve || InTransit`, so the Receive workflow UI disables the confirm button for quarantine boxes. Fix: add `Quarantine` to the `isReceivable` check and update the page error message copy.

**Tech Stack:** C# / .NET 8, xUnit + FluentAssertions + Moq (backend tests), TypeScript / React (frontend)

---

## Files

| Action | File |
|--------|------|
| Modify test | `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByCodeHandlerTests.cs` |
| Modify handler | `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs` |
| Modify UI | `frontend/src/components/pages/TransportBoxReceive.tsx` |

---

## Task 1: Extend the test to cover `Quarantine` as a receivable state

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByCodeHandlerTests.cs:106-142`

- [ ] **Step 1: Add `Quarantine` to the valid-states theory**

Open `GetTransportBoxByCodeHandlerTests.cs`. Find the theory at line 106:

```csharp
[Theory]
[InlineData(TransportBoxState.Reserve)]
[InlineData(TransportBoxState.InTransit)]
public async Task Handle_BoxInValidState_ReturnsSuccessResponse(TransportBoxState state)
```

Replace it with:

```csharp
[Theory]
[InlineData(TransportBoxState.Reserve)]
[InlineData(TransportBoxState.InTransit)]
[InlineData(TransportBoxState.Quarantine)]
public async Task Handle_BoxInValidState_ReturnsSuccessResponse(TransportBoxState state)
```

- [ ] **Step 2: Run the new test case to verify it fails**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTransportBoxByCodeHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: the `Quarantine` case fails with `Expected: True, Actual: False` (isReceivable is false). The existing `Reserve` and `InTransit` cases continue to pass.

---

## Task 2: Fix `isReceivable` in the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs:51-52`

- [ ] **Step 1: Update the receivable check**

Find lines 51-52:

```csharp
// Check if box is in a receivable state (Reserve or InTransit)
var isReceivable = transportBox.State == TransportBoxState.Reserve || transportBox.State == TransportBoxState.InTransit;
```

Replace with:

```csharp
// Check if box is in a receivable state (InTransit, Reserve, or Quarantine)
var isReceivable = transportBox.State == TransportBoxState.Reserve
    || transportBox.State == TransportBoxState.InTransit
    || transportBox.State == TransportBoxState.Quarantine;
```

- [ ] **Step 2: Build to verify no compilation errors**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run the handler tests to verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTransportBoxByCodeHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: all tests pass including the new `Quarantine` case.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByCodeHandlerTests.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs
git commit -m "fix: include Quarantine in isReceivable check for transport box receive workflow"
```

---

## Task 3: Fix error message copy in the Receive page

**Files:**
- Modify: `frontend/src/components/pages/TransportBoxReceive.tsx:188-192`

The page currently says `"V přepravě" nebo "V rezervě"` but quarantine is now valid too.

- [ ] **Step 1: Update the error message**

Find lines 188-192 in `frontend/src/components/pages/TransportBoxReceive.tsx`:

```tsx
<p>
  Box je ve stavu "{getStateLabel(boxDetails.state || '')}" a nemůže být přijat. 
  Pro příjem musí být box ve stavu "V přepravě" nebo "V rezervě".
</p>
```

Replace with:

```tsx
<p>
  Box je ve stavu "{getStateLabel(boxDetails.state || '')}" a nemůže být přijat.
  Pro příjem musí být box ve stavu "V přepravě", "V rezervě" nebo "V karanténě".
</p>
```

- [ ] **Step 2: Build the frontend**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh/frontend
npm run build 2>&1 | tail -20
```

Expected: build succeeds with no TypeScript errors.

- [ ] **Step 3: Run frontend lint**

```bash
npm run lint 2>&1 | tail -20
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/TransportBoxReceive.tsx
git commit -m "fix: update receive page error message to include quarantine state"
```

---

## Verification

**Manual check (staging):**
1. Find or create a transport box in `Quarantine` state.
2. Navigate to the Receive page (`/logistics/transport/receive`).
3. Scan (or type) the box code.
4. Confirm the box loads with the "Potvrdit příjem" button **enabled** (not the red not-receivable alert).
5. Confirm receipt — box should transition to `Received`, then eventually to `Stocked`.

**Note:** The terminal receive component (`frontend/src/components/terminal/TransportBoxReceive.tsx`) already lists "V karanténě" in its not-receivable message and relies on the same `isReceivable` flag — no changes needed there.
