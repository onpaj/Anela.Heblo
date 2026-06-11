Plan written to `artifacts/feat-arch-review-gridlayouts-gridlayoutdto-us/plan.r1.md`.

**Summary:** 9 bite-sized TDD tasks covering the refactor:
- **Tasks 1–3:** Create the new `Infrastructure/` folder with `StoredColumnState`, `StoredGridLayout` (with empty-list default), and `GridLayoutStoredMapper`.
- **Tasks 4–5:** TDD the Save handler — failing FR-2 shape test first, then switch serialization to `StoredGridLayout`.
- **Tasks 6–7:** Add the FR-4 legacy-shape read test, then switch the Get handler to deserialize `StoredGridLayout` and project to the DTO with entity-sourced `GridKey`/`LastModified`.
- **Tasks 8–9:** Full GridLayouts test sweep, then solution-wide `dotnet build` / `dotnet format` / `dotnet test` gate.

The plan locks in the architecture review's four key decisions (folder name `Infrastructure/`, dedicated static mapper, explicit `[JsonPropertyName]` with no `JsonSerializerOptions`, empty-list default for `Columns`), confirms `ResetGridLayoutHandler` requires no change (FR-6 verified at line 30), and traces every spec requirement to a specific task in a coverage table. No placeholders, all code blocks complete, consistent method signatures across tasks.

Per pipeline instructions, skipping the execution choice prompt.