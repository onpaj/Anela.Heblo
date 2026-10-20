# Code Review: adapter-and-di

## Summary
The implementation matches the task context's specified code exactly: the new
`ExpeditionListArchiveBlobStoreAdapter` correctly implements `IExpeditionListArchiveBlobStore` by
delegating to `IBlobStorageService`, and the DI registration in `FileStorageModule` is wired
correctly as a Singleton. Build and `dotnet format --verify-no-changes` both pass.

## Review Result: PASS

### task: adapter-and-di
**Status:** PASS

## Docs to Update
(None — internal adapter/DI wiring only, no public behaviour or documented pattern changed)

## Overall Notes
Adapter is `internal sealed`, matching `KnowledgeBaseLeafletSourceAdapter`'s visibility and shape
as instructed. `ListBlobsAsync` correctly maps `BlobItemInfo` → `ExpeditionBlobItem` field-for-field.
`Singleton` lifetime matches `IBlobStorageService`'s own registration. No concerns.
