## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsReader.cs:28` — the new-user branch builds a throwaway `UserDashboardSettings` entity only to map it to DTOs; mapping tiles directly would avoid the allocation. Minor; shared defaults logic is intentionally single-sourced.

Verified: build clean, Dashboard-filtered tests pass (203). Query path performs no writes or locking; mutator provisions before taking the non-reentrant lock; "anonymous" fallback preserved.
