# Code Review: feat-4154 (Decouple OrgChart Adapter Deserialization from Application Contracts)

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature-branch diff against `spec.r1.md` (FR-1/FR-2/FR-3) and
`design.r1.md`'s field-mapping table.

- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs` —
  the four new `internal sealed class` POCOs match FR-1 exactly (no `BaseResponse`
  dependency, referenced only from `OrgChartService.cs`).
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` — now
  deserializes into `OrgChartJsonModel` instead of `OrgChartResponse` (FR-2.1), keeps
  the same null-check/message/no-inner-exception shape (FR-3), and the
  `MapOrganization`/`MapPosition`/`MapEmployee` mapping methods implement the
  field-mapping table in `design.r1.md` field-for-field with no swapped/missing
  members. `try`/`catch` structure and both exception-wrap messages are byte-for-byte
  unchanged; `LogInformation` still reads the final mapped `orgChart`, not the
  intermediate JSON model, per FR-3.
  - Checked one edge case closely: `ParentPositionId`, `Position.Url`, and
    `Employee.Url` are nullable in the target DTOs with no `= string.Empty` default,
    yet the mapping coalesces null/absent values to `string.Empty` for them too. This
    is a literal, deliberate implementation of `design.r1.md`'s field-mapping table
    (rows for `ParentPositionId`/`Url`) and of spec FR-2.4 ("missing/null string
    fields ... map to string.Empty"), both already approved in the architecting/
    designing phases — not a developer error or a deviation from the approved design.
    The only known consumer (`frontend/src/pages/orgChartUtils.ts`) treats
    `parentPositionId`/`url` as falsy for both `null` and `""`, so no observed
    consumer breaks. Not blocking; flagging only for the record in case a stricter
    "byte-identical to pre-refactor output" reading of FR-2's acceptance criterion
    matters to a future consumer.
- Independently verified: `dotnet build` (repo root, `Anela.Heblo.sln`) — 0 errors;
  `dotnet format --verify-no-changes` on the three changed/added files — no changes
  needed; `dotnet test --filter "FullyQualifiedName~OrgChart"` — 15/15 pass (includes
  `OrgChartServiceTests`, `GetOrganizationStructureHandlerTests`,
  `OrgChartModuleValidationTests`).
- Scope discipline: no changes to `IOrgChartService`, `OrgChartModule`, DI
  registration, `BaseResponse`, or any `*Dto` definition — matches spec's Out of
  Scope list.

## Docs to Update
(none — no public behavior change, no new pipeline/agent concepts)
