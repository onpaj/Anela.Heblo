**File:** `CLAUDE.md:15`

```
- `docs/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic
```

**Evidence:** the file actually lives at `docs/architecture/📘 Architecture Documentation – MVP Work.md` (confirmed with `find . -iname "*Architecture Doc*"`). Every other entry in the same list — `docs/architecture/filesystem.md`, `docs/development/setup.md`, `docs/design/ui_design_document.md`, `docs/testing/playwright-e2e-testing.md`, `docs/integrations/mcp-server.md`, etc. — resolves correctly; this is the only broken path, missing the `architecture/` segment.

**Why it matters:** `CLAUDE.md` explicitly instructs — "Read the relevant doc **before** implementation work touches that area. No architectural changes without consulting these first." This is the *first* document listed under Architecture. Anyone or any agent following that instruction literally for an architecture question hits a file-not-found on the top entry of the map meant to prevent exactly that kind of miss.

**Suggested direction:** fix the path to `docs/architecture/📘 Architecture Documentation – MVP Work.md`.

