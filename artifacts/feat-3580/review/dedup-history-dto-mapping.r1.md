# Code Review: dedup-history-dto-mapping

## Summary
The implementation adds `PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)` as a single-expression static factory mirroring `PurchaseOrderLineDto.FromLine`'s style, and replaces all three duplicated inline mapping blocks in `CreatePurchaseOrderHandler`, `GetPurchaseOrderByIdHandler`, and `GetPurchaseOrderHistoryHandler` with calls to it. The diff is confined exactly to the four files specified, the `.OrderByDescending(h => h.ChangedAt)` ordering is preserved immediately after `.Select(...)` in `GetPurchaseOrderByIdHandler`, and no other lines were touched.

## Review Result: PASS

### task: dedup-history-dto-mapping
**Status:** PASS

## Overall Notes
Independent verification performed against the committed diff (`git show bc91130e`) rather than relying solely on the implementation summary:
- `grep -rn "new PurchaseOrderHistoryDto" backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` returns zero matches — the only remaining construction is inside `FromDomain` in `Contracts/`.
- `FromDomain` maps all six fields (`Id`, `Action`, `OldValue`, `NewValue`, `ChangedAt`, `ChangedBy`) 1:1 with no transformation, matching the spec exactly.
- `GetPurchaseOrderByIdHandler`'s critical ordering constraint is satisfied: `.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt).ToList()` — mapping strictly before ordering.
- The added `PurchaseOrderHistoryDto.cs` file retains the pre-existing no-trailing-newline convention, consistent with `PurchaseOrderLineDto.cs` in the same folder — not a stray diff artifact.
- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (pre-existing nullable-reference warnings in unrelated test files are unchanged by this diff).
- `dotnet test ... --filter "FullyQualifiedName~CreatePurchaseOrderHandlerTests|FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"` passes: 14/14 (0 failed), confirming behavior-preserving refactor. No `GetPurchaseOrderByIdHandlerTests.cs` exists in the repo, consistent with the spec's note.
- No test files were modified, per the "run unmodified" requirement.
- `PurchaseOrderLineDto.cs` and the domain entity `PurchaseOrderHistory.cs` were left untouched, as required.

No documentation updates are needed — this is a self-contained internal refactor with no behavior or contract change.
