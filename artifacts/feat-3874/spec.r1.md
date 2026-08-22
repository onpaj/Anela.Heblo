# Specification: CatalogDocuments — Shared PIF short-code prefix derivation

## Summary
`UploadPifDocumentHandler` and `ListPifDocumentsHandler` in the CatalogDocuments feature each independently reimplement the same rule for deriving a PIF folder's matching prefix from a product code (first 6 characters, or the whole code if shorter, followed by `__`). This spec factors that rule into a single shared helper — mirroring the existing `MaterialFilenameBuilder` pattern already used by the sibling Materials flow — so the two handlers can never silently disagree on which SharePoint/OneDrive folder a product's PIF documents live in.

## Background
CatalogDocuments manages PIF (product information file) and Material documents stored in SharePoint/OneDrive via `ICatalogDocumentsStorage`. Folders are located by matching a name prefix derived from a product code.

Today:
- `UploadPifDocumentHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs:30-33`) computes `prefix = "{ProductCode[..6] or ProductCode}__"`.
- `ListPifDocumentsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs:29-32`) computes the identical expression independently.

This is not incidental string formatting — the truncation length (6 characters) is a business rule that determines which SharePoint folder is authoritative for a given product's PIF documents. Because the rule is duplicated rather than shared, a future change to one handler and not the other would let uploads and listings silently target different folders, with no error surfaced (both handlers would still find *some* folder via `FindFolderAsync`, just not necessarily the same one).

The sibling Materials flow already avoids this problem: its equivalent filename-construction rule lives in a single shared `MaterialFilenameBuilder.Build(...)` static helper (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`), called from both its upload and list paths. The PIF flow is the outlier within its own feature and should follow the same pattern.

## Functional Requirements

### FR-1: Shared PIF prefix derivation helper
Introduce a single helper that encapsulates the PIF short-code/prefix rule: take the first 6 characters of a product code, or the whole code if it is shorter than 6 characters, and append `__`.

**Acceptance criteria:**
- A new static helper exists in `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/`, following the naming and shape convention of `MaterialFilenameBuilder` (e.g. `PifFolderPrefixBuilder` or equivalent name approved during design), exposing a method that takes a product code string and returns the derived prefix string.
- For a product code of length >= 6, the returned prefix is the first 6 characters followed by `__`.
- For a product code of length < 6, the returned prefix is the full product code followed by `__`.
- The helper contains no I/O, logging, or MediatR/handler dependencies — it is a pure function, consistent with `MaterialFilenameBuilder`.

### FR-2: UploadPifDocumentHandler uses the shared helper
`UploadPifDocumentHandler.Handle` calls the new helper instead of computing `shortCode`/`prefix` inline.

**Acceptance criteria:**
- The inline `shortCode`/`prefix` computation (lines 30-33 of the current handler) is removed and replaced with a call to the shared helper.
- Existing behavior is unchanged: the resulting `prefix` value passed to `_storage.FindFolderAsync` and returned in the `CatalogDocumentFolderNotFound` error payload is identical to today's output for every input.

### FR-3: ListPifDocumentsHandler uses the shared helper
`ListPifDocumentsHandler.Handle` calls the new helper instead of computing `shortCode`/`prefix` inline.

**Acceptance criteria:**
- The inline `shortCode`/`prefix` computation (lines 29-32 of the current handler) is removed and replaced with a call to the shared helper.
- Existing behavior is unchanged: the resulting `prefix` value passed to `_storage.FindFolderAsync` and returned as `ExpectedPrefix` in the response is identical to today's output for every input.

### FR-4: Unit test coverage for the shared helper
Add or extend unit tests to cover the helper directly, and confirm both handlers now delegate to it.

**Acceptance criteria:**
- Unit tests exist for the new helper covering: product code length > 6, product code length exactly 6, and product code length < 6 (including empty string, if that is a reachable input per existing validation — see Open Questions).
- Existing handler-level tests for `UploadPifDocumentHandler` and `ListPifDocumentsHandler` continue to pass unmodified in behavior (prefix values in assertions stay the same), confirming the refactor is behavior-preserving.

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a pure refactor. No change in externally observable behavior (API responses, error codes, folder-matching results) is permitted. No performance-sensitive path is affected — the derivation is O(1) string slicing.

### NFR-2: Consistency with existing conventions
The new helper's location, naming style, and static-method shape should match `MaterialFilenameBuilder` so the CatalogDocuments feature has one consistent pattern for this class of shared derivation logic across both Materials and PIF sub-flows.

## Data Model
No data model changes. No new entities, DTOs, or persisted fields are introduced. The helper operates purely on the existing `ProductCode` string already present on `UploadPifDocumentRequest` and `ListPifDocumentsRequest`.

## API / Interface Design
No public API surface changes. Endpoints, request/response DTOs, and error codes (`ErrorCodes.CatalogDocumentFolderNotFound`) are unchanged. The only interface addition is the new internal static helper class/method in `CatalogDocuments/Infrastructure/`, used exclusively by the two handlers listed above.

## Dependencies
- Depends only on existing code in `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/` — no new external libraries or services.
- No dependency on `ICatalogDocumentsStorage`, SharePoint/Graph, or any I/O.

## Out of Scope
- Changing the truncation length (6 characters) or the prefix format (`{shortCode}__`) — this spec only relocates the existing rule, it does not alter it.
- Unifying the PIF and Materials prefix/filename rules into a single shared abstraction — they encode different business rules (PIF: fixed-length short code; Materials: `{TYPE}__{lot}__{commonName}{ext}`) and should remain separate helpers, mirroring the issue's suggested direction ("alongside or analogous to `MaterialFilenameBuilder`").
- Any change to `FindFolderAsync`, `ICatalogDocumentsStorage`, or the folder-not-found error path.
- Retroactively reconciling any PIF folders that may already be mismatched on SharePoint/OneDrive due to the pre-refactor duplication — this is a code-level fix, not a data-remediation task.

## Open Questions
None. Assumption made and noted here: the exact name of the new helper class/method (e.g. `PifFolderPrefixBuilder.BuildPrefix(productCode)`) is left for the architecture/design phase to finalize, consistent with existing naming conventions in the `Infrastructure/` folder; FR-1 states the required shape and behavior, not the literal identifier.

## Status: COMPLETE
