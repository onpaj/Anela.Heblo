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
