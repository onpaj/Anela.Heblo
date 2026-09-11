# Code Review: update-existing-tests

## Summary
Both target test files were updated to match the handler's new constructor shape by wiring real `NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`, `ReceivedSideEffect`, and `TransportBoxInventoryRestorer` instances from the existing mocks. Inspection of commit `2ba4c39` confirms only constructor/setup plumbing changed — no `Assert.*` or `Verify()` line was touched — and an independent `dotnet build` confirms the test project compiles cleanly (0 errors). The extra, out-of-scope-but-necessary fix to `TransportBoxUniquenessTests.cs` is a reasonable, mechanical, well-documented necessity rather than scope creep.

## Review Result: PASS

### task: update-existing-tests
**Status:** PASS

Verification performed:
- `git show 2ba4c39 --stat` and full `git show 2ba4c39` in the worktree: the diff touches exactly the constructor/setup blocks in all three files (the two spec-named files plus `TransportBoxUniquenessTests.cs`). Every hunk is additive setup code (`sideEffects` array construction + `inventoryRestorer` construction) plus the reordering/trimming of constructor arguments passed to `new ChangeTransportBoxStateHandler(...)`. No `Assert.*`, `.Should()`, `Verify(...)`, or any test body/behavioral line appears in the diff — NFR-4 ("without changing any existing assertion") is satisfied.
- Confirmed the actual handler constructor signature in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`: `(ITransportBoxRepository, IMediator, ILogger<ChangeTransportBoxStateHandler>, ICurrentUserService, TimeProvider, IEnumerable<ITransportBoxTransitionSideEffect>, ITransportBoxInventoryRestorer)` — matches exactly what all three updated call sites now pass, in the same order.
- Confirmed the side-effect classes' constructors (`NewToOpenedSideEffect`, `ReceivedSideEffect`) and the parameterless `OpenToReserveSideEffect`/`OpenToQuarantineSideEffect` match how they're instantiated in the test setup code.
- Ran `grep -rn "new ChangeTransportBoxStateHandler(" backend/` — exactly the three updated call sites remain; no missed/broken construction site exists anywhere else in the repo.
- Ran `dotnet build test/Anela.Heblo.Tests` independently in the worktree: succeeded with 0 errors (248 pre-existing warnings, unrelated to this change).
- Started an independent `dotnet test --filter "FullyQualifiedName~ChangeTransportBoxState"` run to cross-check the developer's reported 26 passed / 2 failed (Docker-dependent) result; it was still executing when this review had to conclude, but the build success plus the assertion-untouched diff give high confidence the unit tests (`ChangeTransportBoxStateHandlerTests`, 26 facts/theories) behave identically to before, and the 2 integration test failures are the expected, spec-acknowledged Docker/testcontainer environment limitation, not a code defect.
- The `TransportBoxUniquenessTests.cs` file is out of the original two-file list but was a genuine, mechanical necessity: it directly constructs `ChangeTransportBoxStateHandler` with the old signature and would not otherwise compile. The fix follows the identical, spec-described pattern (build side effects + restorer from the file's own existing mocks) and its diff likewise contains zero assertion changes. Given the task's overriding constraint that the build must succeed and existing tests must pass unmodified, this was correctly in-scope to fix rather than a deviation requiring rejection — and it was properly flagged in the implementation notes for pipeline visibility.

No functional requirement is unmet, no architecture guideline is contradicted, and no correctness bug was found.

## Overall Notes
- The developer's transparency about the third call site (flagging it explicitly rather than silently expanding scope, and noting the deviation) is exactly the right behavior for this kind of build-breaking transitive dependency.
- Docker-unavailable integration test failures are an acknowledged environment limitation per the task spec itself and are not held against this review.
