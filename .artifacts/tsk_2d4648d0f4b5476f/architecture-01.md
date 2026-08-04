# Architecture review: `ManufactureSettingsController` auth fix

## Verdict

**Approved as designed.** The design in `design-01.md` matches every codebase
invariant it invokes, and I independently re-verified each claim against the
current source rather than trusting the artifact. No changes required before
implementation.

## What was checked and confirmed

1. **Current controller state** (`ManufactureSettingsController.cs`) — matches
   the design's "Before" snippet exactly: `ControllerBase`, `[AllowAnonymous]`,
   raw `Task<GetManufactureSettingsResponse>` return, no `[FeatureAuthorize]`.

2. **`BaseApiController`** — abstract, extends `ControllerBase`, exposes
   `Logger` and `HandleResponse<T>(T response) where T : BaseResponse`, which
   maps `Success == false` to a status code via `ErrorCodes`'
   `HttpStatusCodeAttribute`. `GetManufactureSettingsResponse : BaseResponse`
   — the design's proposed action signature
   (`Task<ActionResult<GetManufactureSettingsResponse>>` + `HandleResponse`)
   type-checks against this contract.

3. **Sibling pattern** (`ManufacturedProductInventoryController`) — confirmed
   class-level `[FeatureAuthorize(Feature.X)]` + `BaseApiController` +
   per-action `HandleResponse` is the live, repeated pattern across the
   Manufacture module, not an invented one.

4. **`FeatureAuthorizeAttribute`** — sealed `AuthorizeAttribute` subclass with
   a public `Feature` property and `Roles` computed via
   `AccessRoles.For(feature, level)`, default `AccessLevel.Read`. The design's
   test (`attribute!.Feature.Should().Be(Feature.Manufacture_ManufactureOrders)`)
   is a valid, reflectable check.

5. **Feature bucket choice** — `Feature.Manufacture_ManufactureOrders` is
   already the class-level gate on `ManufactureOrderController.cs:19` (with
   several `AccessLevel.Write` overrides on individual write actions), and is
   registered in `AccessMatrix.generated.cs` (`FeatureDefinition` with
   `HasWrite: true`, and the `/manufacturing/orders` `MenuPath` requiring it at
   `Read`). Reusing it for a Read-level settings lookup is consistent with the
   existing matrix and requires no codegen change — confirms the plan's
   scope call was correct.

6. **`GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize`** — reread
   in full. It walks every non-abstract `ControllerBase` in the API assembly
   and flags any role-gated action lacking `[FeatureAuthorize]` (class or
   method level), skipping actions with `[AllowAnonymous]` or non-role
   `AuthorizeAttribute`s (policy/scheme-based). Once `[AllowAnonymous]` is
   dropped and the class carries `[FeatureAuthorize(...)]`, this controller
   automatically satisfies the check — the design's claim of a "free assist"
   is correct, no edit to this test file needed.

7. **`MockAuthenticationHandler`** — read in full. It unconditionally builds a
   `SuperUser` + `BaseRole` claims principal and returns
   `AuthenticateResult.Success`, with **no branch on the incoming
   `Authorization` header**. This independently confirms the design's central
   test-design finding: the existing `GetSettings_ShouldBeReachableAnonymously`
   test (verified present, asserting `HttpStatusCode.OK` after clearing the
   auth header) cannot distinguish `[AllowAnonymous]` from gated-but-mocked-
   super-user, so it is not testing what its name claims. Replacing it with a
   reflection-based test is the only sound option inside this harness.

8. **Reflection-test precedent** — read both
   `DiagnosticsControllerTests.cs` (`Controller_ShouldRequireAuthorization`,
   `Actions_ShouldNotAllowAnonymous`) and
   `GridLayoutsControllerAuthorizationTests.cs` in full. The design's proposed
   `ManufactureSettingsControllerAuthorizationTests` follows the same
   reflection-on-attributes shape used repo-wide for this exact class of
   check. Minor, inconsequential divergence: Diagnostics checks for the base
   `AuthorizeAttribute`; the design checks for `FeatureAuthorizeAttribute`
   specifically — strictly stronger since `FeatureAuthorizeAttribute : AuthorizeAttribute`, and appropriate here since a `Feature` value is exactly what needs asserting.

9. **Existing content tests** — `GetSettings_ShouldReturnSuccessAndCorrectContentType`
   and `GetSettings_ShouldExposeManufactureGroupIdField` read in full; both
   use the plain (mock-super-user) `_client`, so they're unaffected by adding
   the gate, as the design states.

## Alignment with existing patterns

No deviation from module conventions. This brings the one outlier controller
back to the shape every other controller in `API/Controllers` already uses:
`BaseApiController` + class-level `[FeatureAuthorize]` + `HandleResponse`.
Nothing about this change introduces a new pattern, a new `Feature` value, or
touches generated code (`Feature.generated.cs` / `AccessRoles.generated.cs` /
`AccessMatrix.generated.cs` are correctly left untouched).

## Risks and mitigations

- **Risk:** choosing the wrong `Feature` bucket could lock out a legitimate
  consumer outside Manufacture Orders. **Mitigation:** already covered by the
  design/plan's frontend grep (`useManufactureSettings.ts` is the only
  consumer, on the Orders creation/detail flow) — the architecture layer adds
  no new mitigation here since this is a data question, not a structural one.
  Low residual risk given `SuperUser` and any future broader role can still be
  granted access via `AccessRoles.For`.
- **Risk:** relying on reflection tests instead of live 401/403 HTTP tests
  means a future refactor could silently remove the attribute without an
  HTTP-level regression test catching it. **Mitigation:** already
  structurally covered — `GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize`
  runs repo-wide and would fail if `[FeatureAuthorize]` were later dropped
  without adding `[AllowAnonymous]` back; this is the same safety net every
  other controller in the codebase relies on, so no gap is introduced
  specifically by this change.
- **Risk:** none identified around `HandleResponse` — `GetManufactureSettingsHandler`
  is out of scope and unchanged, so behavior for the current always-`Success`
  path is identical (still 200), and the design correctly scopes the
  `Success == false` handling as latent/future-proofing rather than an active
  behavior change today.

## Prerequisites before implementation

None outstanding. All open questions from `plan-01.md` are resolved in
`design-01.md` and independently confirmed here:
- Feature bucket: `Feature.Manufacture_ManufactureOrders` (confirmed correct
  and already in the matrix).
- Test-authentication mechanism: `HebloWebApplicationFactory` /
  `MockAuthenticationHandler` always mocks `SuperUser`, confirmed by reading
  the handler directly — reflection-based tests are the right (and only
  viable) tool here.

Implementation may proceed exactly per `design-01.md`.
