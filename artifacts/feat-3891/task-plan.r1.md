# Validate LLM-returned PhotoId against the batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `PhotobankAutoTagJob` from applying AI tags to a `PhotoId` that wasn't actually part of the batch sent to the LLM, closing a silent-mistagging/prompt-injection hole.

**Architecture:** In `ProcessBatchAsync`, build a `HashSet<int>` of the batch's photo ids alongside the existing `batchIds` list, and pass it into `ApplyTagsForPhotoAsync`, which now rejects (and logs a warning for) any `AutoTagResult` whose `Id` isn't in that set before doing any of its existing tag-vocabulary filtering or `AddPhotoTagAsync` calls. No new files, no interface/schema changes — a single-file, single-class change plus tests.

**Tech Stack:** C# / .NET 8, xUnit + Moq + FluentAssertions (existing test stack in `Anela.Heblo.Tests`).

---

### task: reject-out-of-batch-ids

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs:102-170`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`

- [ ] **Step 1: Write the failing tests**

Add two new `[Fact]` methods to `PhotobankAutoTagJobTests` (place them after `ExecuteAsync_AppliesValidTagsAndDropsHallucinations`, following the existing test's setup style — `SetupChatResponse`, `_photoTagRepo` mocks, `CreateJob()`):

```csharp
[Fact]
public async Task ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags()
{
    // Arrange — the only candidate sent to the LLM has id 42, but the model's response
    // claims an id (999) that was never part of this batch (hallucination, or a
    // FolderPath/FileName-driven prompt-injection attempt). That id must never be written.
    var candidate = new PhotoAutoTagCandidate(Id: 42, FolderPath: "/photos", FileName: "product.jpg");

    _tagRepo
        .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TagCount> { new(1, "kosmetika", 3) });

    _autoTagRepo
        .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PhotoAutoTagCandidate> { candidate })
        .ReturnsAsync(new List<PhotoAutoTagCandidate>());

    // LLM response references id=999, which was never sent — only id=42 was in the batch.
    SetupChatResponse("""{"results":[{"id":999,"tags":["kosmetika"]}]}""");

    _photoTagRepo
        .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    _photoTagRepo
        .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _photoTagRepo
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _autoTagRepo
        .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var job = CreateJob();

    // Act
    await job.ExecuteAsync(CancellationToken.None);

    // Assert — no PhotoTag is ever written for the out-of-batch id, or for any id at all
    // (the batch had exactly one candidate, id 42, and the LLM never returned a result for it).
    _photoTagRepo.Verify(
        r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // The batch (id 42) is still stamped as processed, regardless of the rejected result —
    // stamping behavior for ids actually sent must be unchanged (spec FR-1).
    _autoTagRepo.Verify(
        r => r.StampAutoTaggedAtAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 42),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult()
{
    // Arrange — two candidates sent (10 and 11); LLM returns a valid result for 10 and a
    // result for an id (55) that belongs to neither candidate in this batch.
    var candidates = new List<PhotoAutoTagCandidate>
    {
        new(Id: 10, FolderPath: "/photos", FileName: "a.jpg"),
        new(Id: 11, FolderPath: "/photos", FileName: "b.jpg"),
    };

    _tagRepo
        .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TagCount> { new(1, "kosmetika", 3) });

    _autoTagRepo
        .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(candidates)
        .ReturnsAsync(new List<PhotoAutoTagCandidate>());

    SetupChatResponse("""{"results":[{"id":10,"tags":["kosmetika"]},{"id":55,"tags":["kosmetika"]}]}""");

    _photoTagRepo
        .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    _photoTagRepo
        .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _photoTagRepo
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _autoTagRepo
        .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var job = CreateJob();

    // Act
    await job.ExecuteAsync(CancellationToken.None);

    // Assert — only id 10 (in the batch) gets a PhotoTag written; id 55 (not in the batch) never does.
    _photoTagRepo.Verify(
        r => r.AddPhotoTagAsync(
            It.Is<PhotoTag>(pt => pt.PhotoId == 10 && pt.TagId == 1 && pt.Source == PhotoTagSource.AI),
            It.IsAny<CancellationToken>()),
        Times.Once);
    _photoTagRepo.Verify(
        r => r.AddPhotoTagAsync(
            It.Is<PhotoTag>(pt => pt.PhotoId == 55),
            It.IsAny<CancellationToken>()),
        Times.Never);
    _photoTagRepo.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankAutoTagJobTests"`

Expected: `ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags` and `ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult` FAIL — today `ApplyTagsForPhotoAsync` writes a `PhotoTag` for `result.Id` unconditionally, so `AddPhotoTagAsync` is called for the out-of-batch id (999 / 55) when it should not be. All other existing tests in the file still PASS (this task must not touch their behavior).

- [ ] **Step 3: Implement the batch-id guard**

In `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`, change `ProcessBatchAsync` to build a `HashSet<int>` alongside the existing `batchIds` list and pass it to `ApplyTagsForPhotoAsync`:

```csharp
private async Task ProcessBatchAsync(
    IReadOnlyList<PhotoAutoTagCandidate> batch,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
{
    var batchIds = batch.Select(p => p.Id).ToList();
    var batchIdSet = new HashSet<int>(batchIds);

    var systemPrompt = BuildSystemPrompt(tagsByName.Keys);
    var userPrompt = BuildUserPrompt(batch);

    ChatResponse response;
    try
    {
        response = await _chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt),
            ],
            new ChatOptions { ModelId = _options.Model },
            ct);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "LLM call failed for batch of {Count} photos; skipping batch", batch.Count);
        return;
    }

    var raw = response.Text ?? string.Empty;
    var fallback = new AutoTagLlmPayload { Results = [] };
    var parsed = JsonResponseParser.ParseOrFallback(raw, fallback, _logger);

    foreach (var result in parsed.Results ?? [])
    {
        await ApplyTagsForPhotoAsync(result, batchIdSet, tagsByName, ct);
    }

    await _photoTagRepository.SaveChangesAsync(ct);
    await _autoTagRepository.StampAutoTaggedAtAsync(batchIds, DateTime.UtcNow, ct);
    _cache.Invalidate();
}
```

Change `ApplyTagsForPhotoAsync` to take the batch id set as a parameter and reject anything outside it before doing anything else:

```csharp
private async Task ApplyTagsForPhotoAsync(
    AutoTagResult result,
    HashSet<int> batchIds,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
{
    if (!batchIds.Contains(result.Id))
    {
        _logger.LogWarning(
            "AI tagging result id {ResultId} is not in the sent batch (batch size {BatchSize}); dropping result.",
            result.Id, batchIds.Count);
        return;
    }

    var validTags = (result.Tags ?? [])
        .Where(name => tagsByName.ContainsKey(name))
        .Distinct()
        .Take(_options.MaxTagsPerPhoto)
        .ToList();

    foreach (var tagName in validTags)
    {
        var tagId = tagsByName[tagName];

        if (await _photoTagRepository.PhotoTagExistsAsync(result.Id, tagId, ct)) continue;

        await _photoTagRepository.AddPhotoTagAsync(
            new PhotoTag
            {
                PhotoId = result.Id,
                TagId = tagId,
                Source = PhotoTagSource.AI,
                CreatedAt = DateTime.UtcNow,
            },
            ct);
    }
}
```

No other method in the file changes. `ExecuteForPhotosAsync` needs no edits — it already calls `ProcessBatchAsync` per sub-batch.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankAutoTagJobTests"`

Expected: PASS — all tests in `PhotobankAutoTagJobTests` green, including the two new ones and the six pre-existing ones (`ExecuteAsync_WhenStatusCheckerReturnsFalse_DoesNotCallLlmOrRepository`, `ExecuteAsync_WhenNoPendingPhotos_DoesNotCallLlm`, `ExecuteAsync_StampsAllPhotosInBatch_EvenWhenLlmReturnsEmptyTags`, `ExecuteAsync_RespectsMaxTagsPerPhoto_Cap`, `ExecuteAsync_AppliesValidTagsAndDropsHallucinations`, `ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse`, `ExecuteAsync_LlmReturnsStringEncodedId_AppliesTagsSuccessfully`).

- [ ] **Step 5: Run the full backend build and formatter**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`

Expected: build succeeds with no errors; `dotnet format --verify-no-changes` reports no formatting differences (if it does, run `dotnet format` without `--verify-no-changes` and re-stage).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
git commit -m "fix(photobank): reject AI tag results whose id is outside the sent batch"
```
