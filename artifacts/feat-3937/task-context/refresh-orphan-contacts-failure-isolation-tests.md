### task: refresh-orphan-contacts-failure-isolation-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs` (append methods inside the existing class, after the two skip tests from the previous task)
- Test: same file

- [ ] **Step 1: Write the failing tests (FR-3, FR-4, and continue-after-failure)**

Add these three `[Fact]` methods inside the `RefreshOrphanContactsHandlerTests` class (after `Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound`):

```csharp
    [Fact]
    public async Task Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // reset tracking noise from seeding, isolate the handler's own effect

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        // Without ChangeTracker.Clear(), the entity mutated by `local.ContactId = remote.ContactId`
        // just before the throw would still be tracked as Modified. An empty tracker here proves
        // the handler's catch block actually called _db.ChangeTracker.Clear().
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upsert boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextItem_AfterAFailure()
    {
        // Arrange
        SetupIds("conv-fail", "conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-2" });

        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-fail"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));
        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1); // conv-ok was still processed despite conv-fail's exception
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: all 5 tests present; the 3 new ones should PASS against the existing handler implementation (the brief documents this behavior as already implemented — these tests lock it in). If `Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows` or `Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows` fails on the `ChangeTracker.Entries().Should().BeEmpty()` assertion, STOP and report: it means `_db.ChangeTracker.Clear()` is missing or not effective, which is the exact regression the brief warns about — do not weaken the assertion to make it pass.

- [ ] **Step 3: Confirm all 5 tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (5 passed, 0 failed)

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler per-item failure isolation"
```

---

