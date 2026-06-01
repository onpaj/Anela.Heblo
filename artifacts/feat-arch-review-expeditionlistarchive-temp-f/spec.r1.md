# Specification: Fix Temp File Leak in ReprintExpeditionListHandler

## Summary
`ReprintExpeditionListHandler` leaks one zero-byte temp file per invocation because `Path.GetTempFileName()` creates a file on disk that is never deleted (the handler only cleans up a different `.pdf` path it constructs by string concatenation). Replace the path construction with `Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")` so no extra file is created and the existing cleanup logic remains effective.

## Background
The reprint flow writes a PDF blob to disk and hands the path to a CUPS print sink, then deletes the temp file via `DeleteTempFile`. The current code uses:

```csharp
var tempFile = Path.GetTempFileName() + ".pdf";
```

`Path.GetTempFileName()` has two side effects: it generates a unique name (e.g. `tmpXXXX.tmp`) **and** immediately creates a zero-byte file at that path. Appending `".pdf"` produces a new string (`tmpXXXX.tmp.pdf`) referencing a different, non-existent path. The handler subsequently writes and deletes the `.pdf` path, leaving the original `.tmp` file orphaned on disk. Over time `/tmp` fills with empty `.tmp` files — one per reprint, regardless of success or failure.

The CUPS sink may rely on the `.pdf` extension, so the chosen replacement must preserve it.

## Functional Requirements

### FR-1: Eliminate orphaned temp file on every reprint
Replace the temp path construction in `ReprintExpeditionListHandler` so that exactly one file is created on disk per reprint, and that file is the one the handler manages end-to-end.

**Acceptance criteria:**
- After a successful reprint, no new files remain in the system temp directory attributable to this handler.
- After a failed reprint (exception during write or print), no new files remain in the system temp directory attributable to this handler (existing cleanup semantics preserved).
- The path passed to the CUPS sink still ends in `.pdf`.
- The path is unique per invocation (no collisions under concurrent reprints).

### FR-2: Preserve existing cleanup behavior
The existing `DeleteTempFile` helper must continue to be the single cleanup point. No new cleanup paths or shutdown hooks are introduced.

**Acceptance criteria:**
- `DeleteTempFile` is still invoked in the same control-flow locations (success and failure paths) as before the change.
- No additional `try/finally` or disposal scaffolding is added beyond what already exists.

### FR-3: No behavioral change to print output
The reprint operation must continue to produce the same PDF content delivered to the same sink with the same filename semantics from the consumer's perspective. Only the path-generation mechanism changes.

**Acceptance criteria:**
- Reprint produces an identical PDF payload at the temp path.
- The CUPS sink receives a path with a `.pdf` extension and successfully prints in manual verification.

## Non-Functional Requirements

### NFR-1: Performance
Negligible impact. `Guid.NewGuid()` + `Path.Combine` is faster than `Path.GetTempFileName()` (which performs a file-system create) and removes one disk I/O per reprint.

### NFR-2: Security
- Path is constructed from `Path.GetTempPath()` and a server-generated GUID — no user input flows into the path, so no path-traversal exposure is introduced.
- Default permissions on files created in the temp directory remain unchanged (still inherits OS/process defaults).
- Random GUID (`N` format, 32 hex chars) makes the filename unguessable, mitigating any race with a malicious local actor pre-creating the same path.

### NFR-3: Reliability
The fix removes a silent resource leak that degrades server health over time. No new failure modes are introduced; if `File.WriteAllBytes` (or equivalent) fails on the constructed path, the failure surfaces immediately just as today.

## Data Model
None. This change does not touch the domain model, database, or DTOs.

## API / Interface Design
No public API, contract, or UI change. The change is internal to `ReprintExpeditionListHandler.cs` at the temp-file allocation line (currently at `ReprintExpeditionListHandler.cs:29`).

**Before:**
```csharp
var tempFile = Path.GetTempFileName() + ".pdf";
```

**After:**
```csharp
var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
```

## Dependencies
- `System.IO.Path` (BCL) — already in use.
- `System.Guid` (BCL) — already available.
- No new packages, services, or feature flags.

## Testing Strategy
- **Unit test:** Add or update a test that invokes `ReprintExpeditionListHandler` and asserts:
  1. The temp directory contains no leftover files matching the handler's pattern after both success and failure paths.
  2. The path handed to the print sink ends in `.pdf`.
  Since the handler currently depends on a CUPS sink and file system, the test should inject/stub the sink and use a temp-directory snapshot (count files before/after) to verify no leak.
- **Manual verification:** Trigger a reprint end-to-end on a dev environment, confirm the PDF prints, and confirm no new `.tmp` or `.pdf` files remain in `/tmp` after the operation completes.

## Out of Scope
- Refactoring `DeleteTempFile` or the broader handler structure.
- Switching to a stream-based approach that bypasses disk entirely (would require CUPS sink changes).
- Auditing other call sites in the codebase for similar `Path.GetTempFileName()` misuse — that is a separate sweep.
- Adding background cleanup of pre-existing orphaned temp files left by previous deployments.
- Changing the temp directory location or introducing a configurable temp path.

## Open Questions
None.

## Status: COMPLETE