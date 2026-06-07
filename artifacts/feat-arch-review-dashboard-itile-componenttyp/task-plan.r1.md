Plan saved to `artifacts/feat-arch-review-dashboard-itile-componenttyp/plan.r1.md`.

**Summary:** A 7-task implementation plan covering removal of `ComponentType` across 19 production files and 7 test files. The plan is structured around the single hard constraint of this refactor — removing a member from the `ITile` interface and the `TileMetadata` positional record breaks all consumers at compile time, so intermediate builds will not succeed. Tasks 1–6 are coordinated edit units; Task 7 runs `dotnet build` + `dotnet format` + `dotnet test` + `npm run build` + `npm run lint` as the single validation gate, then commits all 28 files together. The plan includes:

- Pre-flight grep with the exact 35 expected hits (19 in `src/`, 16 in `test/`)
- File-by-file `old_string`/`new_string` snippets with line numbers
- Use of `replace_all: true` for the multi-site fixture files (`TestTiles.cs`, `TileExtensionsTests.cs`, `TileRegistryValidationTests.cs`)
- Per arch-review Decision 3: only the asserting *line* is removed from the three `Metadata_*HasCorrectValues` tests, not the test methods
- Explicit verification that `DashboardTileDto` and the generated TypeScript client are unchanged (guarantees FR-6 byte-identical JSON)
- Self-review table mapping every spec FR/NFR to its implementing task