# Honest Dependency in ShoptetApiExpeditionListSource Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the misleading `IEshopOrderClient` constructor parameter (immediately downcast to the concrete `ShoptetOrderClient`) with the concrete `ShoptetOrderClient` itself, so the dependency is honest at the signature and the silent `InvalidCastException` risk is removed.

**Architecture:** Pure backend refactor inside the `Anela.Heblo.Adapters.ShoptetApi` adapter assembly. The class calls three methods (`GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, `SetAdditionalFieldAsync`) that exist only on the concrete `ShoptetOrderClient`, not on `IEshopOrderClient`, and `ShoptetApiExpeditionListSource` is the only in-process consumer of those methods. Both types live in the same adapter assembly, so depending on the concrete type is the appropriate Clean Architecture choice (matches the previously-shipped `ShoptetApiPackingOrderClient` refactor from `docs/superpowers/plans/2026-05-24-honest-dependency-shoptetapi-packing-order-client.md`). DI registration and existing tests already construct/expose a concrete `ShoptetOrderClient`, so this change is source-compatible — only the SUT constructor needs to change.

**Tech Stack:** .NET 8, C#, xUnit, Moq, `dotnet build` / `dotnet format` / `dotnet test`.

---

## File Map

**Modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
  - Constructor parameter type: `IEshopOrderClient` → `ShoptetOrderClient` (line 34).
  - Remove the downcast on line 42 (`_client = (ShoptetOrderClient)client;` → `_client = client;`).
  - Remove the stale comment on lines 20–21 explaining why the cast was safe — the cast is gone, the comment lies.
  - Let `dotnet format` decide whether to remove the now-unused `using Anela.Heblo.Application.Features.ShoptetOrders;` on line 4. **Do not hand-edit the using list.**

**Verify only (must continue to compile and pass — do NOT edit):**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — already registers `ShoptetOrderClient` as a concrete type via `AddHttpClient<ShoptetOrderClient>(…)` (line 37); `IEshopOrderClient` is forwarded via `services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())` (line 43). The new constructor signature is already satisfiable.
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` — already constructs the SUT by passing a real `ShoptetOrderClient` (lines 43, 110, 665). The refactor is source-compatible.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — same (line 178, 207).
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — interface is untouched; other consumers (`BlockOrderProcessingHandler`, `ScanPackingOrderHandler`) remain unaffected.

**Do NOT create any new files.**

---

## Task 1: Capture green baseline before changing anything

**Files:**
- No edits.

- [ ] **Step 1: Build the solution to confirm a clean starting point**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: Build succeeds. 0 errors, 0 warnings related to `ShoptetApiExpeditionListSource`.

- [ ] **Step 2: Run the targeted tests to confirm they are currently green**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"
```
Expected: All tests pass. Note the pass count for comparison after the refactor.

If either of the above fails, **stop**. Do not refactor on top of a red baseline. Report the failure and pause for guidance.

---

## Task 2: Replace the interface parameter with the concrete type and drop the cast & stale comment

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:20-49`

This task is a single behavior-preserving refactor. There is no new test to write — the existing tests are the regression net (verified green in Task 1). The class will be re-validated by re-running those tests in Task 4.

- [ ] **Step 1: Remove the stale comment on lines 20–21**

Before:
```csharp
public class ShoptetApiExpeditionListSource : IPickingListSource
{
    // ShoptetOrderClient is the only implementation of IEshopOrderClient — safe to cast
    // within this adapter assembly to access expedition-specific methods not on the interface.
    private readonly ShoptetOrderClient _client;
```

After:
```csharp
public class ShoptetApiExpeditionListSource : IPickingListSource
{
    private readonly ShoptetOrderClient _client;
```

- [ ] **Step 2: Change the constructor parameter type from `IEshopOrderClient` to `ShoptetOrderClient`**

Before (line 34):
```csharp
    public ShoptetApiExpeditionListSource(
        IEshopOrderClient client,
        TimeProvider timeProvider,
```

After:
```csharp
    public ShoptetApiExpeditionListSource(
        ShoptetOrderClient client,
        TimeProvider timeProvider,
```

- [ ] **Step 3: Remove the downcast on line 42**

Before:
```csharp
        _client = (ShoptetOrderClient)client;
```

After:
```csharp
        _client = client;
```

- [ ] **Step 4: Do NOT touch the using directives by hand**

Do not delete `using Anela.Heblo.Application.Features.ShoptetOrders;` on line 4. `dotnet format` (Task 3) will remove it if it is now unused. Hand-editing the using list violates the project's "surgical changes" rule. If `dotnet format` does *not* remove it, that means another symbol in the file still needs it — leave it alone.

- [ ] **Step 5: Build the modified project to confirm the refactor compiles**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: Build succeeds. 0 errors. If the build fails because a consumer outside the two known test files passes a non-`ShoptetOrderClient` `IEshopOrderClient` into this constructor, **stop and report it** — that is a real consumer we did not find during architecture review and must be discussed before deciding the next step.

---

## Task 3: Run `dotnet format` to apply analyzer fixes (including the potentially unused using)

**Files:**
- Modify (formatter only): `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/**`

- [ ] **Step 1: Run `dotnet format` on the adapter project**

Run:
```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: Completes without errors. Any whitespace and unused-using fixes are applied automatically.

- [ ] **Step 2: Inspect the formatter's effect**

Run:
```bash
git diff backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
```
Expected: The diff shows the three intentional changes from Task 2 (comment removed, parameter type changed, cast removed) and — likely — the removal of `using Anela.Heblo.Application.Features.ShoptetOrders;` on line 4. Any other change in this file is unintended; revert it before continuing.

- [ ] **Step 3: Re-run `dotnet format` to confirm idempotency**

Run:
```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj --verify-no-changes
```
Expected: Exits with code 0 (no further changes required).

---

## Task 4: Re-run the targeted tests and the full suite to confirm behavior is preserved

**Files:**
- No edits.

- [ ] **Step 1: Re-run the targeted tests**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"
```
Expected: All tests pass with the same pass count noted in Task 1. If any test fails or the count drops, **stop and report** — the refactor was supposed to be source-compatible and behavior-preserving. Do not edit tests to "fix" them. A failure here is a signal that something deeper is going on.

- [ ] **Step 2: Run the full backend test suite to catch any unexpected consumer**

Run:
```bash
dotnet test backend/Anela.Heblo.sln
```
Expected: All tests pass. The most likely failure mode (if any) is the architecture-boundary tests in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` flagging a new edge — review the failure carefully; if it is legitimately a new (forbidden) cross-module dependency, **stop and report** rather than mutate the boundary test. In this case the change is intra-assembly (the SUT and `ShoptetOrderClient` already live in the same adapter), so no boundary test should react.

---

## Task 5: Commit the change

**Files:**
- No edits.

- [ ] **Step 1: Stage the modified file(s)**

Run:
```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
```

If `dotnet format` modified any additional file in the adapter project, also stage it:
```bash
git status
```
Stage any additional adapter files only — do not stage unrelated changes.

- [ ] **Step 2: Create the commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: inject concrete ShoptetOrderClient into ShoptetApiExpeditionListSource

The constructor previously declared a dependency on IEshopOrderClient and
immediately downcast to ShoptetOrderClient — a silent InvalidCastException
risk and a misleading signature, since the methods called
(GetOrdersByStatusAsync, GetExpeditionOrderDetailAsync, SetAdditionalFieldAsync)
exist only on the concrete client. Both types live in the same adapter
assembly, so depending on the concrete type is appropriate (matches the
ShoptetApiPackingOrderClient refactor from 2026-05-24).

DI registration and existing tests already supply a concrete ShoptetOrderClient
— no DI or test edits required. Stale comment about the safe cast removed.
EOF
)"
```
Expected: Commit succeeds. Pre-commit hooks (if any) pass.

- [ ] **Step 3: Verify the working tree is clean**

Run:
```bash
git status
```
Expected: `nothing to commit, working tree clean`.

---

## Spec Coverage Self-Check

| Spec requirement | Where covered |
|---|---|
| FR-1: Replace interface parameter with concrete type | Task 2, Steps 2–3 |
| FR-1 (sub): backing field type already `ShoptetOrderClient` | Verified during planning (line 22 already concrete); no edit needed |
| FR-1 (sub): downcast removed | Task 2, Step 3 |
| FR-1 (sub): no other behavioral changes | Task 2 scope is explicit; Task 4 re-runs tests to confirm |
| FR-2: DI still resolves | Task 4, Step 2 (full suite includes app startup paths); arch review confirms DI is already correct |
| FR-3: Tests still compile and pass | Task 1 (baseline) + Task 4 (post-change); arch review confirms tests already pass a real `ShoptetOrderClient` |
| FR-4: Behavior preserving | Task 4, Steps 1–2; no public method signature changes |
| NFR-1 Performance | N/A — type-only change, no runtime behavior delta |
| NFR-2 Security | N/A — no auth/secrets/data exposure changes |
| NFR-3 Maintainability | Achieved by Task 2 (honest signature + stale comment removed) |
| NFR-4 Build & validation | Task 1 (baseline build), Task 2 Step 5 (post-refactor build), Task 3 (`dotnet format` clean), Task 4 (tests pass) |
| Arch review: remove stale comment | Task 2, Step 1 |
| Arch review: do not hand-edit using directives | Task 2, Step 4 (explicit instruction) |
| Out of scope: expand `IEshopOrderClient` | Not touched |
| Out of scope: refactor other consumers | Not touched |
| Out of scope: rename anything | Not touched |
| Out of scope: change business logic | Not touched |
