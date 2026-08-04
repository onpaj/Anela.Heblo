# Architecture check: DepartmentsController MediatR migration

## Verdict

**Approve the design with one required addition.** The MediatR/DTO migration itself (FR-1–FR-3) is correct and verified against the live codebase. But the design carries forward a second, separate invariant violation that the plan/design authors waved off as "out of scope" without noticing it's actually a small, in-scope fix given the pattern already established in the same module: **the new `GetDepartmentsHandler` still reaches directly into another module's domain namespace (`Domain.Features.InvoiceClassification.IDepartmentClient`)**, which is exactly what `docs/architecture/development_guidelines.md`'s Forbidden-Practices table bans ("Direct access to another module's entities", "Communication between modules exclusively through contracts/"). This is fixable with a ~15-line addition, using a pattern that already exists verbatim one folder over (`IGraphService`). See §3.

## 1. Verified against the codebase (all claims in plan-01.md / design-01.md checked directly)

| Claim | Verified |
|---|---|
| `DepartmentsController` injects `IDepartmentClient`, returns `ActionResult<IEnumerable<Department>>`, extends `ControllerBase` | ✅ `DepartmentsController.cs:8-22`, byte-for-byte as quoted |
| `IDepartmentClient`/`Department` live in `Domain.Features.InvoiceClassification` | ✅ confirmed, both files read |
| Module map assigns `DepartmentsController.cs` to module 35 ("Users, Identity & Org Chart") alongside `UserManagementController`/`OrgChartController` | ✅ `module-map.md` §35 |
| `Domain/Features/InvoiceClassification/` is owned by a **different** module (21, "Invoice Classification") | ✅ `module-map.md` §21 — confirms the cross-module reference is real, not a misreading |
| `GetGroupMembersHandler`/`GetGroupMembersResponse` shape (BaseResponse, catch-all → `ErrorCodes.InternalServerError`, `List<UserDto>`) | ✅ matches design's template exactly |
| `BaseApiController.HandleResponse<T>` maps `Success`/`ErrorCode` → HTTP status via `HttpStatusCodeAttribute` reflection | ✅ read in full |
| `UserManagementController` pattern (`BaseApiController`, `IMediator`, `HandleResponse`) | ✅ matches design's target shape |
| MediatR handlers auto-registered via assembly scan, no manual DI needed | ✅ `ApplicationModule.cs:64` |
| `ErrorCodes.InternalServerError` exists and maps to HTTP 500 | ✅ `ErrorCodes.cs:32-33` |
| DTOs must be classes, not records; "DTOs defined in API" and "Business logic in Controller class" are forbidden practices | ✅ `development_guidelines.md:38` and general project rule |
| `useDepartments.ts` currently does a manual `fetch` against `/api/departments`, typed `Promise<Department[]>` | ✅ read in full — matches design quote exactly |
| Generated client currently types `departments_GetDepartments(): Promise<Department[]>`, and no other file calls that generated method | ✅ confirmed via grep; only `useDepartments.ts` hits the endpoint |
| JSON responses use camelCase (`success`, `departments`, not `Success`/`Departments`) | ✅ `Program.cs:151-154` — `AddJsonOptions` only adds the enum-string converter, no `PropertyNamingPolicy` override, so ASP.NET Core's default camelCase policy applies. The design's example JSON (`"departments": [...]`) and the frontend unwrap (`body.departments`) are consistent with this. |

No factual claim in the plan or design was found to be wrong. This step is about invariants, and one was missed — covered next.

## 2. Alignment with ADR-003 and the Forbidden-Practices table — mostly correct, one gap

The rewritten controller (FR-3) is a clean fix for the originally-reported defect: no business logic in the controller, no raw domain entity on the wire, standard `BaseApiController`/`HandleResponse` dispatch. This closes the ADR-003 violation cleanly and is the right shape — verified against `UserManagementController.GetGroupMembers`, which is the most current, most consistently-applied instance of the pattern in this module.

One note for whoever implements this: `OrgChartController` (also module 35) does **not** follow this pattern — it extends `ControllerBase` directly and hand-rolls a try/catch instead of using `HandleResponse`. That's a pre-existing inconsistency within module 35, not something this task should fix, but it means "matches the sibling pattern" has two siblings that disagree. The design correctly picked `UserManagementController` (the `BaseApiController`/`HandleResponse` version) as the template — that's the newer, DRYer, and more consistently-documented convention (`BaseApiController`'s own doc comment describes exactly this contract), so that choice is correct.

Minor, non-blocking: `UserManagementController` and `OrgChartController` both decorate actions with `[ProducesResponseType(...)]` for Swagger; the design's `DepartmentsController` snippet omits these. Not an invariant, just a consistency nit — worth adding while touching the file.

## 3. Required addition: the design still violates "communication between modules exclusively through contracts"

`development_guidelines.md` Forbidden-Practices table lists two rules the new handler runs straight into:

- **"Direct access to another module's entities"** — Violates boundaries, tight coupling
- **"Communication between modules exclusively through `contracts/`"** (Mandatory Rules, line 12)

`GetDepartmentsHandler` (design-01.md, FR-2) constructor-injects `IDepartmentClient` directly from `Anela.Heblo.Domain.Features.InvoiceClassification` — a domain type owned by **module 21** ("Invoice Classification" per `module-map.md`), not module 35. The design's own scope note frames this as pre-approved by the arch-review issue text ("the separate question of which module should own the Departments endpoint is out of scope"), but that sentence is about *endpoint ownership* (should `DepartmentsController` move to module 21?) — it does not bless a module-35 Application-layer class holding a hard dependency on another module's domain interface. Those are different questions, and only the first was scoped out.

**Why this isn't hypothetical or gold-plating:** the exact fix pattern already exists in this module, for this exact handler's sibling. `GetGroupMembersHandler` does not depend on Microsoft Graph's SDK or any other module's domain type — it depends on `IGraphService`, an interface **declared inside `UserManagement/Services/`** (module 35's own folder), implemented by an adapter (`GraphService`/`MockGraphService` in `Anela.Heblo.Adapters.Microsoft365`, DI-registered in `Microsoft365AdapterServiceCollectionExtensions.cs`). That is precisely the "Consumer (A) defines the contract" rule from `development_guidelines.md:231` in action, one folder away from where the new code is going.

`IDepartmentClient` is a good candidate for the same treatment — it's a 2-method interface (`GetDepartmentsAsync`, `GetDepartmentByIdAsync`), already implemented by `FlexiDepartmentClient` in the Flexi adapter and registered in `FlexiAdapterServiceCollectionExtensions.cs:86`. Note it's also consumed by `DepartmentSyncService` in that same adapter (an existing cross-cutting use, not exclusive to module 21), so this isn't about relocating `IDepartmentClient` — only about not having module 35's Application layer name a foreign module's domain type directly.

**Concrete addition to the plan (small, in scope):**
1. Add `IDepartmentQueryService` (name to taste) under `Application/Features/UserManagement/Services/` — mirrors `IGraphService`'s location — with a single method, e.g. `Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken ct)`, or better, return `DepartmentDto` directly to avoid leaking `Domain.Features.InvoiceClassification.Department` into the module-35 Application layer at all.
2. Implement it with a thin adapter class (e.g. `FlexiDepartmentQueryService : IDepartmentQueryService`) that wraps the existing `IDepartmentClient` — this can live in `Anela.Heblo.Adapters.Flexi` next to `FlexiDepartmentClient`, or as a one-line pass-through registered alongside it in `FlexiAdapterServiceCollectionExtensions.cs`.
3. `GetDepartmentsHandler` depends on `IDepartmentQueryService`, not `IDepartmentClient`. No `using Anela.Heblo.Domain.Features.InvoiceClassification;` anywhere in `Anela.Heblo.Application`.

This is additive to FR-2, not a redesign — same request/response/handler shape, same error handling, just one extra thin interface + adapter class, following an established local precedent. If the implementer judges this adds too much for the ticket's scope, the alternative is to explicitly re-scope the issue (comment/ticket update) rather than silently ship a documented Forbidden Practice — but given the fix is this cheap and the precedent this exact, I recommend doing it now rather than opening a follow-up.

## 4. Everything else — no further findings

- Frontend plan (FR-4): sound. Only consumer of the endpoint is `useDepartments.ts`'s manual fetch; the generated client's `departments_GetDepartments()` has no other call sites, so regeneration is mechanical with no other file to touch.
- No persistence/schema/migration impact — confirmed `IDepartmentClient`/domain `Department` untouched.
- No new DI registration needed for the MediatR handler (assembly scan already covers `Anela.Heblo.Application`).
- Decision to leave `GetDepartmentByIdAsync` and `[FeatureAuthorize]` alone: correct, no controller/no existing auth requirement to preserve or extend — adding either would be scope creep.

## Prerequisites before implementation

None blocking — the codebase state matches what plan-01.md and design-01.md assumed. Only open item is the §3 addition, which should be folded into FR-2 rather than tracked separately.
