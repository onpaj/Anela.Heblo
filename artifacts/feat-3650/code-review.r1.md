## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/api/generated/api-client.ts:517-561,690-768` — This diff bundles unrelated generated-client additions (`meetingTasks_UpdateStatus`, `UpdateTranscriptStatusRequest/Response`, `MeetingTranscriptDto.participants`) that have nothing to do with the Marketing Calendar move feature. Likely just a byproduct of when the OpenAPI client was regenerated relative to `main`. Not a bug, but worth a sanity check that these lines are genuinely already on `main`/another merged branch and not an accidental cross-feature leak before opening the PR.
