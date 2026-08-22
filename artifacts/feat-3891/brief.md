**Evidence**

`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs:143-170` (`ApplyTagsForPhotoAsync`):

```csharp
private async Task ApplyTagsForPhotoAsync(
    AutoTagResult result,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
{
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

Called from `ProcessBatchAsync` (`PhotobankAutoTagJob.cs:102-141`):

```csharp
var raw = response.Text ?? string.Empty;
var parsed = JsonResponseParser.ParseOrFallback(raw, fallback, _logger);
foreach (var result in parsed.Results ?? [])
{
    await ApplyTagsForPhotoAsync(result, tagsByName, ct);
}
```

`result.Id` (`AutoTagResult.Id`, `PhotobankAutoTagJob.cs:197-205`) is parsed straight out of the LLM's JSON response text and used verbatim as the `PhotoId` for a new `PhotoTags` row. `batchIds` — the IDs actually sent to the model (`PhotobankAutoTagJob.cs:107`) — is computed but only used for `StampAutoTaggedAtAsync(batchIds, ...)`; it is never used to validate `result.Id`.

`PhotoId` has a real FK to `Photos` (`backend/src/Anela.Heblo.Persistence/Photobank/PhotoConfiguration.cs:26-29`), so a completely nonexistent ID fails the batch's `SaveChangesAsync`. But any ID belonging to a *different, real* photo — plausible since these are small sequential integers and the model is never told to stay within the batch — gets a new AI-sourced tag applied silently.

**Rule violated**

`CLAUDE.md` / `docs/architecture/development_guidelines.md:77` states: *"Request DTOs must not carry client-settable `UserId` / `ModifiedBy` — these are server-resolved, never trusted from the client (spoofing hole)."* The same principle — don't let an external, untrusted actor pick which row gets mutated — is violated here for an even less trustworthy source: LLM completion output.

**Why it matters, concretely**

- `BuildUserPrompt` (`PhotobankAutoTagJob.cs:184-188`) embeds each photo's `FolderPath`/`FileName` directly into the LLM prompt. These strings originate from SharePoint file/folder names, content anyone with upload access to the configured drive controls — a crafted name is a plausible prompt-injection vector to make the model emit an `id` outside the batch, silently tagging an unrelated photo.
- Even without adversarial intent, a model hallucinating/miscopying an id (a routine LLM failure mode) corrupts a real record with no detection anywhere in the pipeline.
- The job runs nightly, unattended (`CronExpression = "0 4 * * *"`), so mistagging accumulates silently.

**Suggested direction**

Intersect `result.Id` against `batchIds` (or the `candidates` list in `ExecuteForPhotosAsync`) before calling `ApplyTagsForPhotoAsync`, dropping/logging any result whose `id` falls outside the set actually sent to the model in that call.

