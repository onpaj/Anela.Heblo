All 5 tasks are complete. Here's the summary:

---

**Implementation complete: Decouple Photobank MediatR Requests from HTTP Body DTOs**

**What was done:**
- Created 3 new `*Body` DTOs in `Contracts/`: `AddRootBody`, `AddRuleBody`, `RetagPhotosBody`
- Updated `PhotobankController` actions `AddRoot`, `AddRule`, `RetagPhotos` to bind `[FromBody]` to the new DTOs and map them to the existing MediatR `*Request` types via inline object initializers
- Added `PhotobankControllerBodyMappingTests.cs` with 3 tests (all pass) — each verifies field-by-field mapping and action-specific HTTP status codes (201 `CreatedAtAction` for Add* endpoints, 202 `Accepted` for RetagPhotos)
- Regenerated the OpenAPI TypeScript client — generated class names updated (`*Request` → `*Body`) but JSON wire shapes are byte-for-byte identical

**4 commits on this branch** (`AddRootBody`, `AddRuleBody`, `RetagPhotosBody`, `chore: regenerate client`).

**Status: DONE_WITH_CONCERNS** — two pre-existing issues that pre-date this branch: 161 frontend lint errors (in test utilities) and 3 Docker/Testcontainers test failures (infrastructure, not code). Neither was introduced by this change.