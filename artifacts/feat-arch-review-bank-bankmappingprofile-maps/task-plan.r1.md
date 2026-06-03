Self-review of the plan against the spec:

**Spec coverage:** FR-1 (Task 2 Step 1), FR-2 (Task 1 tests for `"OK"` and `"Failed"`; `null` ImportResult is unreachable through the domain setter so deliberately not tested at mapping layer — called out in coverage map), FR-3 (frozen-files list + checklist), FR-4 (real `IMapper`, not `Mock<IMapper>`), NFR-1..3 (no behavioural surface), NFR-4 (Task 2 Step 6 `git diff` check on `frontend/src/api-client/`). All architecture review decisions (keep `CreateMap`, dedicated profile test, no `ImportStatus.Success` rename) honoured.

**Placeholder scan:** No TBDs, no "add appropriate error handling", no "similar to Task N", no orphan symbols. Every code block is complete and copy-pasteable.

**Type consistency:** Type names (`BankStatementImport`, `BankStatementImportDto`, `BankMappingProfile`, `IMapper`, `MapperConfiguration`), constructor signature (`(string transferId, DateTime statementDate)`), and property usage (`ImportResult` settable, `ErrorType` get-only) all match what I verified by reading the actual files.

Plan saved to `docs/superpowers/plans/2026-06-03-remove-dead-bankmappingprofile-errortype-formember.md`. Two surgical tasks: (1) add `BankMappingProfileTests` against a real `IMapper`, commit; (2) drop the `.ForMember(...)` chain on `BankMappingProfile.cs:12`, build, re-run tests, verify no OpenAPI client diff, format, commit.