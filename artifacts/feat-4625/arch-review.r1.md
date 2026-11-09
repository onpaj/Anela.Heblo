# Architecture Review: Photobank delta 410 recovery

## Skip Design: true
Backend-only bug fix, no UI.

## Architectural Fit Assessment
Fits the existing Application-layer job + Adapter service split. The adapter already surfaces HTTP failures as `HttpRequestException` (with `StatusCode` populated by `EnsureSuccessStatusCode` on .NET 8). The job owns the `DeltaLink` persistence state, so token-reset policy belongs in the job.

## Proposed Architecture

### Component Overview
`PhotobankIndexJob.IndexRootAsync` -> `IPhotobankGraphService.GetDeltaAsync` (adapter, unchanged) -> Graph. Job gains a small private helper `GetDeltaWithResyncAsync(root, ct)`.

### Key Design Decisions

#### Decision 1: Where to handle 410
**Options considered:** (a) adapter catches 410 and retries without token; (b) job catches and retries.
**Chosen approach:** (b) in the job.
**Rationale:** The job owns and persists `root.DeltaLink`; clearing it in memory keeps state consistent, the adapter stays a thin stateless client, and the behaviour is unit-testable with the existing `Mock<IPhotobankGraphService>` (the adapter has no HTTP-level tests).

#### Decision 2: Detection
Catch `HttpRequestException ex when (ex.StatusCode == HttpStatusCode.Gone && !string.IsNullOrEmpty(root.DeltaLink))`. No new exception type, no interface change.

## Implementation Guidance

### Directory / Module Structure
Edit only:
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`
- `docs/processes/sync-photobank-index.md`

### Interfaces and Contracts
```csharp
private async Task<GraphDeltaResult> GetDeltaWithResyncAsync(PhotobankIndexRoot root, CancellationToken ct)
{
    try { return await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct); }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone && !string.IsNullOrEmpty(root.DeltaLink))
    {
        _logger.LogWarning(ex, "Delta token for root {RootId} is expired (410 Gone). Discarding it and performing a full resync.", root.Id);
        root.DeltaLink = null;
        return await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, null, ct);
    }
}
```
`IndexRootAsync` replaces its direct `GetDeltaAsync` call with this helper.

### Data Flow
Stored token -> 410 -> clear token -> full enumeration -> normal upsert path -> save new `DeltaLink`. If the retry fails the cleared (null) value is not saved; the DB still holds the old token, so the next night repeats the 410 and a retry, which is acceptable.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Full enumeration reports no deletions, leaving orphan photo rows | Low | Documented quirk; out of scope |
| Large full enumeration is memory heavy | Low | Same as first-run behaviour today |
| Token URL in logs | Low | Log root id only; exception message from `EnsureSuccessStatusCode` contains no URL |
| Stale in-memory null saved by later SaveChanges when retry fails | Low | Only matters if a later save occurs in the same scope; a null token simply forces a full walk, which is safe |

## Specification Amendments
None.

## Prerequisites
None.
