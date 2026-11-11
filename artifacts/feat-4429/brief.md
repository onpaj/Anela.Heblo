# [arch-review] Logistics: TransportBoxController mixes two incompatible error-response patterns (issue #4429)

Module: Logistics

`TransportBoxController` (`backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`) uses two patterns to convert handler responses to HTTP responses:

- Pattern A, `HandleResponse(response)` (project-standard helper from `BaseApiController`): `ChangeTransportBoxState`, `OpenOrResumeBoxByCode`.
- Pattern B, manual inline `if (!response.Success) return BadRequest(response); return Ok(response);`: `CreateNewTransportBox`, `AddItemToBox`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`, `GetTransportBoxByCode`.

`GetTransportBoxById` uses a third variant (`response.TransportBox == null` -> `NotFound`), possibly intentional.

Why it matters: enhancements to `HandleResponse` silently skip Pattern B endpoints; drift is indistinguishable from intent; more per-endpoint code.

Suggested fix: replace the five Pattern B endpoints with `return HandleResponse(response);`. If `GetTransportBoxByCode` / `GetTransportBoxById` need specific status mappings, extend `HandleResponse` or add a thin override, not inline logic.
