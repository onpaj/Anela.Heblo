### task: add-error-code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:276-283`

This is a pure enum-member addition — no branching logic, so there is no meaningful unit test to write for it in isolation (its effect is exercised by the tests added in later tasks, once the handler/controller/MCP tool actually use it). Skipping a dedicated test here is deliberate, not an oversight.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Find the `// Leaflet module errors (25XX)` block:
  ```csharp
      // Leaflet module errors (25XX)
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletChunkNotFound = 2501,
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletFeedbackNotFound = 2502,
      [HttpStatusCode(HttpStatusCode.Conflict)]
      LeafletFeedbackAlreadySubmitted = 2503,

      // Photobank errors (26XX)
  ```
- [ ] Step 2: Add a new member immediately after `LeafletFeedbackAlreadySubmitted = 2503,`, before the `// Photobank errors (26XX)` comment:
  ```csharp
      // Leaflet module errors (25XX)
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletChunkNotFound = 2501,
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletFeedbackNotFound = 2502,
      [HttpStatusCode(HttpStatusCode.Conflict)]
      LeafletFeedbackAlreadySubmitted = 2503,
      [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
      LeafletEmptyRetrieval = 2504,

      // Photobank errors (26XX)
  ```
  Do not renumber or reorder any existing members — their numeric values are the wire contract for the generated TypeScript enum.
- [ ] Step 3: Build the backend to confirm the enum still compiles:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Commit.
  ```bash
  git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
  git commit -m "#3478: add ErrorCodes.LeafletEmptyRetrieval (422) for Leaflet empty-retrieval case"
  ```

---
