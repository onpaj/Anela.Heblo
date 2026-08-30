### task: retarget-picking-list-integration-test-to-expedition-picking-request

## Goal

Repoint `PickingListIntegrationTests.cs`'s two references to `PrintPickingListRequest.DefaultSourceStateId` / `.DefaultDesiredStateId` at `ExpeditionPickingRequest`'s constants, and update the adjacent explanatory comment — **before** those constants are removed from `PrintPickingListRequest` in Task 3, so the build stays green at every commit (spec FR-4).

## Files to change

**Edit:**
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`

**Verify only, no change expected:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — still declares its own `DefaultSourceStateId` / `DefaultDesiredStateId` / `DefaultNoteStateId` at this point in the plan (removed in Task 3); this task must not touch it, since both the old and new constant references need to coexist and both compile identically until Task 3 removes the old ones.

**Do not touch:**
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — never references the removed constants by name.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — unaffected (copies runtime values, not the constants).

## Steps

- [ ] **Step 1: Confirm the current consumer sites (baseline)**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "PrintPickingListRequest\.Default\|^using" backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected output:
```
1:using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
2:using Anela.Heblo.Application.Features.ExpeditionList;
3:using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
4:using Anela.Heblo.Application.Features.ExpeditionList.Services;
5:using Anela.Heblo.Application.Features.ShoptetOrders;
6:using Anela.Heblo.Application.Shared.Printing;
7:using Anela.Heblo.Domain.Features.Logistics;
8:using Anela.Heblo.Application.Features.Logistics.Picking;
23:    private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;
88:                DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId,
```

Note `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` is already present (line 3) — the file already references `ExpeditionPickingRequest.DefaultCarriers` on line 89, so no new `using` is needed for this task.

- [ ] **Step 2: Retarget the comment and the `SourceStateId` constant reference**

Use the Edit tool on `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`.

Change:
```csharp
    // Must match PrintPickingListRequest.DefaultSourceStateId (-2 = "Vyřizuje se").
    // Use statusId= query parameter (not status=) — the correct param name supports negative system IDs.
    private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;
```

to:
```csharp
    // Sourced from ExpeditionPickingRequest.DefaultSourceStateId (-2 = "Vyřizuje se").
    // Use statusId= query parameter (not status=) — the correct param name supports negative system IDs.
    private const int SourceStateId = ExpeditionPickingRequest.DefaultSourceStateId;
```

The comment no longer describes a match between two separate declarations (there will only be one after Task 3), so it is reworded to describe the single source instead of a cross-class equivalence.

- [ ] **Step 3: Retarget the `DesiredStateId` constant reference**

In the same file, change:
```csharp
                DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId,
```

to:
```csharp
                DesiredStateId = ExpeditionPickingRequest.DefaultDesiredStateId,
```

This is the line inside the `new PrintPickingListRequest { ... }` object initializer (directly above `Carriers = ExpeditionPickingRequest.DefaultCarriers,`, which is already correctly targeted and untouched).

- [ ] **Step 4: Build the test project to confirm it still compiles**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

Expected: build succeeds with no errors. At this point both `PrintPickingListRequest.DefaultSourceStateId`/`.DefaultDesiredStateId` (still declared) and `ExpeditionPickingRequest.DefaultSourceStateId`/`.DefaultDesiredStateId` exist and are equal (`-2`, `26`), so this is a safe intermediate state — the test now points at the soon-to-be-canonical source while the old declarations still exist as an (about-to-be-removed) safety net.

`PickingListIntegrationTests` itself is `[Trait("Category", "Integration")]` and requires `Shoptet:IsTestEnvironment=true` user secrets plus a live Shoptet store connection (see `ShoptetTestGuard.Assert` in the file) — it cannot and must not be executed as part of this validation. A successful build is the correct and sufficient signal here.

- [ ] **Step 5: Verify no stray references remain**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "PrintPickingListRequest\.Default" backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected output: empty (no matches) — both references now point at `ExpeditionPickingRequest`.

- [ ] **Step 6: Commit**

```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs && git commit -m "$(cat <<'EOF'
test(shoptet): retarget PickingListIntegrationTests to ExpeditionPickingRequest constants

Repoints the two remaining PrintPickingListRequest.DefaultSourceStateId /
.DefaultDesiredStateId references at ExpeditionPickingRequest's constants,
ahead of removing PrintPickingListRequest's own duplicate declarations in
the next commit. Keeps the build green at every commit in this sequence.

No behavior change — both constants currently hold identical values.
EOF
)"
```

---
