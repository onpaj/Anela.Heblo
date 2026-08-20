### task: refresh-orphan-contacts-happy-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs` (append one method)
- Test: same file

No dedicated test exercises the successful-update path today (confirmed: no existing test file references `RefreshOrphanContactsHandler` or `RefreshOrphanContactsRequest` anywhere in `backend/test`). The four branch tests above only exercise skip/failure paths, so `response.Updated++` and the full success flow (`EnrichContactAsync` → `UpsertConversationAsync` → `SaveChangesAsync`) remain uncovered without this task. Per the architecture review's coverage-baseline risk, this task is required to reliably clear the 60% threshold.

- [ ] **Step 1: Write the failing test**

Add this `[Fact]` method inside the `RefreshOrphanContactsHandlerTests` class (after `Handle_ContinuesToNextItem_AfterAFailure`):

```csharp
    [Fact]
    public async Task Handle_IncrementsUpdated_WhenItemProcessedSuccessfully()
    {
        // Arrange
        SetupIds("conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.SkippedNoContactId.Should().Be(0);
        response.Failed.Should().Be(0);
        response.FailedIds.Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok" && c.ContactId == "contact-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: 6 tests present; the new one should PASS against the existing handler (it's an assertion of already-implemented behavior). If it fails, STOP and report the discrepancy rather than adjusting the test to match unexpected behavior (per spec NFR-1).

- [ ] **Step 3: Confirm all 6 tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (6 passed, 0 failed)

- [ ] **Step 4: Run full backend test suite to confirm no regressions**

Run: `dotnet build && dotnet format --verify-no-changes && dotnet test backend/test/Anela.Heblo.Tests`
Expected: build succeeds, formatting clean, full suite PASS.

- [ ] **Step 5: Verify coverage of the target file meets the 60% threshold**

Run the project's coverage tooling against `backend/test/Anela.Heblo.Tests` (per `docs/testing/testing-strategy.md` if a specific coverage command is documented there; otherwise `dotnet test backend/test/Anela.Heblo.Tests /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura` or the equivalent already configured for this repo) and confirm `RefreshOrphanContactsHandler.cs` line coverage is now ≥ 60% (up from 24.5%).
Expected: coverage report shows `RefreshOrphanContactsHandler.cs` at or above 60% line coverage.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler success path, close coverage gap"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (remote contact null path) → `task: refresh-orphan-contacts-skip-tests`, `Handle_IncrementsSkippedNoContactId_WhenRemoteContactIdIsNull`. Covered.
- FR-2 (local conversation not found) → `task: refresh-orphan-contacts-skip-tests`, `Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound`. Covered.
- FR-3 (enrichment exception isolation, incl. `ChangeTracker.Clear()`, loop continuation, `Updated` not incremented) → `task: refresh-orphan-contacts-failure-isolation-tests`, `Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows` + `Handle_ContinuesToNextItem_AfterAFailure`. Covered.
- FR-4 (repository upsert exception isolation) → `task: refresh-orphan-contacts-failure-isolation-tests`, `Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows`. Covered.
- NFR-1 (no behavior change; report discrepancies rather than papering over) → explicit STOP instructions in Step 2 of each task. Covered.
- NFR-2 (mocked collaborators + in-memory `ApplicationDbContext`, deterministic) → all tasks use `Mock<T>` for the three interfaces and `UseInMemoryDatabase` with a unique name per test. Covered.
- NFR-3 (60% coverage threshold) → `task: refresh-orphan-contacts-happy-path-test`, Step 5 explicitly verifies this. Covered.
- Out-of-scope items (no handler refactor, no new integration/E2E tests) → no task modifies `RefreshOrphanContactsHandler.cs` or adds anything outside the one new unit test file. Confirmed not violated.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrasing anywhere in the tasks above; every step shows complete, concrete C# code or an exact runnable command with its expected output.

**3. Type consistency:** `RefreshOrphanContactsHandler`, `RefreshOrphanContactsRequest`, `RefreshOrphanContactsResponse` (`Scanned`, `Updated`, `SkippedNoContactId`, `Failed`, `FailedIds`), `ISmartsuppRepository` (`ListOrphanContactConversationIdsAsync`, `UpsertConversationAsync`, `SaveChangesAsync`), `ISmartsuppApiClient` (`GetConversationAsync`), `ISmartsuppContactEnricher` (`EnrichContactAsync`), `SmartsuppConversation`, `SmartsuppConversationData`, `SmartsuppConversationStatus.Open`, and `ApplicationDbContext.SmartsuppConversations` are used identically (same names, same signatures) across all three tasks — verified directly against the actual source files read during architecture review (`RefreshOrphanContactsHandler.cs`, `ISmartsuppRepository.cs`, `ISmartsuppApiClient.cs`, `ISmartsuppContactEnricher.cs`, `SmartsuppConversation.cs`).
