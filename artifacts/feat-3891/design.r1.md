# Design: Validate LLM-returned PhotoId against the batch before applying auto-tags

## Component Design

### `PhotobankAutoTagJob.ProcessBatchAsync` (modified)
Responsibility unchanged: send one batch of `PhotoAutoTagCandidate` to the LLM, parse the response, apply valid tags, stamp the batch, invalidate the cache.

New responsibility: derive the trusted id set for the batch and pass it down so each result can be validated before being applied.

```
private async Task ProcessBatchAsync(
    IReadOnlyList<PhotoAutoTagCandidate> batch,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
{
    var batchIds = batch.Select(p => p.Id).ToList();          // unchanged — used by StampAutoTaggedAtAsync
    var batchIdSet = new HashSet<int>(batchIds);               // NEW — O(1) membership checks

    ... LLM call + parse unchanged ...

    foreach (var result in parsed.Results ?? [])
    {
        await ApplyTagsForPhotoAsync(result, batchIdSet, tagsByName, ct);   // batchIdSet now passed in
    }

    await _photoTagRepository.SaveChangesAsync(ct);             // unchanged
    await _autoTagRepository.StampAutoTaggedAtAsync(batchIds, DateTime.UtcNow, ct);  // unchanged
    _cache.Invalidate();                                        // unchanged
}
```

### `PhotobankAutoTagJob.ApplyTagsForPhotoAsync` (modified)
Responsibility: apply the LLM's tag suggestions for one result to one photo — now gated by batch membership as its first check, before the existing tag-vocabulary filtering.

```
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
            new PhotoTag { PhotoId = result.Id, TagId = tagId, Source = PhotoTagSource.AI, CreatedAt = DateTime.UtcNow },
            ct);
    }
}
```

No other component (`ExecuteAsync`, `ExecuteForPhotosAsync`, `BuildSystemPrompt`, `BuildUserPrompt`, `AutoTagLlmPayload`, `AutoTagResult`) changes shape — `ExecuteForPhotosAsync` is covered automatically since it calls `ProcessBatchAsync` per sub-batch.

## Data Schemas
No schema changes. No new or modified request/response DTOs, no database migration, no event payload changes. `PhotoTag`, `AutoTagResult`, and `PhotoAutoTagCandidate` are unchanged in shape.
