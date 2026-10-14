# Implementation Plan: Align CreateMarketingActionHandler with UpdateMarketingActionHandler's bulk-assignment APIs

## Goal
Replace `CreateMarketingActionHandler.Handle`'s per-item `AssociateWithProduct`/`LinkToFolder` loops (lines 59–65) with the same `MarketingAction.ReplaceProductAssociations`/`ReplaceFolderLinks` bulk-replace calls `UpdateMarketingActionHandler.Handle` already uses, so both write handlers share one entry point into the domain for setting these two associations.

## Architecture approach
Single-file, same-method-body substitution inside `CreateMarketingActionHandler.Handle` — verbatim the two lines already proven in production via `UpdateMarketingActionHandler`. No new services, interfaces, DTOs, or domain methods. Two intentional behavior changes ride along (documented in spec FR-1/FR-2, both already covered by domain-level tests): case-insensitive product dedup, and folder-link dedup by composite `(FolderKey, FolderType)` instead of `FolderKey` alone. This plan adds handler-level regression tests locking in both, per arch-review's Specification Amendments #1–#2.

## Tech stack
- .NET 8, C# (`ImplicitUsings` is `enable` in `Anela.Heblo.Application.csproj` — `System.Linq`'s `.Select` is available without an explicit `using`, same as `UpdateMarketingActionHandler.cs`)
- xUnit + Moq + FluentAssertions (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`)
- Solution root: `/home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler/Anela.Heblo.sln` — run all `dotnet` commands from the repository root.

## Verified context (re-checked against source on disk)
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs:59-65` — the loops to replace.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:95-98` — the reference implementation (unchanged, not touched by this plan).
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:135` (`ReplaceProductAssociations`) and `:174` (`ReplaceFolderLinks`) — pre-existing, already-public domain methods; not modified by this plan.
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/CreateMarketingActionRequest.cs:27-28` — `AssociatedProducts: List<string>?`, `FolderLinks: List<MarketingFolderLinkRequest>?` (unchanged).
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingFolderType.cs` — enum values used below: `General = 0`, `Campaign = 3`.
- Two live test files confirmed on disk, both compiling against the current handler (different namespaces, no name collision):
  - `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` (namespace `Anela.Heblo.Tests.Application.Marketing`) — already has a `capturedAction`-capture pattern (`Handle_PersistsFolderLinks_WhenProvided`, `Handle_SetsOutlookEventId_WhenBothSucceed`) and mocks `_outlookSync.CreateEventAsync` to succeed by default, so `BuildHandler()` (push enabled by default) works out of the box. This is where the two new regression tests go.
  - `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs` (namespace `Anela.Heblo.Tests.Features.Marketing`) — covers auth + basic create-success; not touched by this plan (no assertion in it depends on case-sensitive dedup or `FolderKey`-only dedup).

---

### task: replace-loops-with-bulk-replace-calls

Replaces the two per-item loops in `CreateMarketingActionHandler.Handle` with the two bulk-replace calls, matching `UpdateMarketingActionHandler` exactly.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` (lines 59–65)

1. Open `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`. Lines 59–65 currently read:

   ```csharp
            if (request.AssociatedProducts?.Any() == true)
                foreach (var product in request.AssociatedProducts.Distinct())
                    action.AssociateWithProduct(product, now);

            if (request.FolderLinks?.Any() == true)
                foreach (var link in request.FolderLinks)
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType, now);
   ```

   Replace exactly this block with:

   ```csharp
            action.ReplaceProductAssociations(request.AssociatedProducts, now);
            action.ReplaceFolderLinks(
                request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
                now);
   ```

   No other line in the file changes — the surrounding `action` construction (lines 49–57) and the Outlook sync block (line 67 onward) are untouched. Do not add a `using System.Linq;` line: `ImplicitUsings` is enabled for this project (confirmed in `Anela.Heblo.Application.csproj`), so `.Select` resolves the same way `UpdateMarketingActionHandler.cs` already relies on it without an explicit `using`.

2. Build the Application project and confirm it succeeds:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

3. Confirm no remaining calls to the per-item methods inside this handler:

   ```bash
   grep -n "AssociateWithProduct\|LinkToFolder" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
   ```

   Expected output: no matches (empty output). (These methods remain defined on `MarketingAction` itself and are out of scope — this grep only checks the handler file.)

4. Stage and commit:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
   git commit -m "Align CreateMarketingActionHandler with Update's bulk-replace domain calls"
   ```

   Expected output: a new commit containing exactly this one file.

---

### task: add-dedup-regression-tests-and-verify-suite

Adds the two handler-level regression tests arch-review's Specification Amendments call for (case-insensitive product dedup; same-key-different-type folder links), then runs the full validation gate.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`

1. Confirm the expected pre-fix behavior would already have passed for the *new* case-insensitive product test (the old per-item loop happened to still land on one entry, per spec FR-1's note that this fix is about the pre-check being case-sensitive, not about a currently-broken dedup) — this step is a no-op sanity read, not a build/test run. Proceed directly to the edit.

2. Open `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`. Locate the end of `Handle_PersistsFolderLinks_WhenProvided` (ends at line 191, just before `Handle_HonorsRuntimePushEnabledFlip_TrueToFalse` starts at line 193):

   ```csharp
            var result = await BuildHandler().Handle(request, CancellationToken.None);

            result.Success.Should().BeTrue();
            capturedAction!.FolderLinks.Should().HaveCount(1);
            capturedAction.FolderLinks.Single().FolderKey.Should().Be("key-1");
            capturedAction.FolderLinks.Single().FolderType.Should().Be(MarketingFolderType.General);
        }

        [Fact]
        public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
   ```

   Insert two new `[Fact]` test methods between the closing `}` of `Handle_PersistsFolderLinks_WhenProvided` and the `[Fact]` attribute of `Handle_HonorsRuntimePushEnabledFlip_TrueToFalse`, so the file reads:

   ```csharp
            var result = await BuildHandler().Handle(request, CancellationToken.None);

            result.Success.Should().BeTrue();
            capturedAction!.FolderLinks.Should().HaveCount(1);
            capturedAction.FolderLinks.Single().FolderKey.Should().Be("key-1");
            capturedAction.FolderLinks.Single().FolderType.Should().Be(MarketingFolderType.General);
        }

        [Fact]
        public async Task Handle_DedupesProductsCaseInsensitively_WhenDuplicateCodesDifferOnlyByCase()
        {
            MarketingAction? capturedAction = null;
            _repository
                .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
                .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

            var request = BuildRequest();
            request.AssociatedProducts = new List<string> { "abc", "ABC" };

            var result = await BuildHandler().Handle(request, CancellationToken.None);

            result.Success.Should().BeTrue();
            capturedAction!.ProductAssociations.Should().HaveCount(1);
            capturedAction.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Fact]
        public async Task Handle_PersistsBothFolderLinks_WhenSameFolderKeyButDifferentFolderType()
        {
            MarketingAction? capturedAction = null;
            _repository
                .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
                .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

            var request = BuildRequest();
            request.FolderLinks = new List<MarketingFolderLinkRequest>
            {
                new() { FolderKey = "key-1", FolderType = MarketingFolderType.General },
                new() { FolderKey = "key-1", FolderType = MarketingFolderType.Campaign },
            };

            var result = await BuildHandler().Handle(request, CancellationToken.None);

            result.Success.Should().BeTrue();
            capturedAction!.FolderLinks.Should().HaveCount(2);
            capturedAction.FolderLinks.Should().Contain(fl =>
                fl.FolderKey == "key-1" && fl.FolderType == MarketingFolderType.General);
            capturedAction.FolderLinks.Should().Contain(fl =>
                fl.FolderKey == "key-1" && fl.FolderType == MarketingFolderType.Campaign);
        }

        [Fact]
        public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
   ```

   Do not change any other test in the file. `System.Linq.Single()` used above is already reachable the same way the existing `Handle_PersistsFolderLinks_WhenProvided` test uses it two lines above (`capturedAction.FolderLinks.Single()`), so no new `using` is needed.

3. Build the test project and confirm it compiles:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

4. Run the two new tests plus the full `CreateMarketingActionHandlerTests` class (both namespaces) to confirm nothing regressed:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
     --filter "FullyQualifiedName~CreateMarketingActionHandlerTests" \
     --no-build
   ```

   Expected output: `Passed!` summary line — all tests in both `Anela.Heblo.Tests.Application.Marketing.CreateMarketingActionHandlerTests` (9 tests: 7 existing + 2 new) and `Anela.Heblo.Tests.Features.Marketing.CreateMarketingActionHandlerTests` (2 tests, unchanged) pass, 0 failed.

5. Run the `UpdateMarketingActionHandlerTests` class as a regression check — it is unmodified by this plan, and its own tests exercising `ReplaceProductAssociations`/`ReplaceFolderLinks` must keep passing unchanged:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
     --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests" \
     --no-build
   ```

   Expected output: `Passed!` summary line, 0 failed.

6. Run `dotnet format`, scoped to the two touched files, to match the project's formatting conventions:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
     --include backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
   dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
     --include backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs
   ```

   Expected output: both exit with no errors (whitespace-only changes at most — re-open both files afterward and confirm the code shown in steps above is still semantically intact).

7. Full-solution build as the final compile gate:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet build Anela.Heblo.sln
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

8. Full backend test suite as the final regression gate:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet test Anela.Heblo.sln
   ```

   Expected output: `Passed!` summary line, 0 failed. This is a behavior-preserving refactor outside the two documented, now-test-locked dedup changes, so no unrelated test should regress.

9. Stage and commit the test file:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   git add backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs
   git commit -m "Add regression tests for CreateMarketingActionHandler dedup behavior changes"
   ```

   Expected output: a new commit containing exactly this one file.

---

## Self-review

**Spec coverage:**
- FR-1 (Create uses `ReplaceProductAssociations`; case-insensitive dedup fix) → `replace-loops-with-bulk-replace-calls` step 1 (the call) + `add-dedup-regression-tests-and-verify-suite` step 2's `Handle_DedupesProductsCaseInsensitively_WhenDuplicateCodesDifferOnlyByCase` (the regression test).
- FR-2 (Create uses `ReplaceFolderLinks`; composite-key dedup change) → `replace-loops-with-bulk-replace-calls` step 1 (the call) + `add-dedup-regression-tests-and-verify-suite` step 2's `Handle_PersistsBothFolderLinks_WhenSameFolderKeyButDifferentFolderType` (the regression test).
- NFR-1 (behavioral equivalence elsewhere: auth check, Outlook ordering, error mapping, compensation, logging/response shape) → verified by running the full existing `CreateMarketingActionHandlerTests` suites unmodified (task 2, step 4) — none of those tests were touched, so a regression there would show up as a new failure.
- NFR-2 (no new dependencies/interfaces) → satisfied as written; no new `using`, service, or domain method introduced.
- Arch-review Specification Amendment #1 (confirm both test files are live, update whichever compile) → confirmed both compile (different namespaces, no collision) in "Verified context" above; only the `Application/Marketing` file needed edits, `Features/Marketing` file has no assertion depending on the old behavior.
- Arch-review Specification Amendment #2 (add explicit regression tests for both behavior changes) → task 2, step 2.

**Placeholder scan:** no "TBD"/"add appropriate error handling"/"handle edge cases"/"similar to Task N" phrasing anywhere above; every code-bearing step shows exact before/after text; every command step shows the exact command and expected output.

**Type consistency:** `ReplaceProductAssociations(request.AssociatedProducts, now)` and `ReplaceFolderLinks(request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)), now)` are copied verbatim from `UpdateMarketingActionHandler.cs:95-98` (confirmed by direct read) — no signature drift. `ProductCodePrefix`, `FolderKey`, `FolderType` property names in the new tests match `MarketingAction.cs`'s domain method bodies exactly (confirmed by direct read of `MarketingAction.cs:104-127,135-199`).

## Status: COMPLETE
