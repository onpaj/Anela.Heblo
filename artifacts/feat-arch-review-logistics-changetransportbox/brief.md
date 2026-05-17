## Module
Logistics

## Finding
The request and response for the `ChangeTransportBoxState` use case are located directly in the `UseCases/` root folder, breaking the pattern established by every other use case in the same module:

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateRequest.cs` — namespace: `Anela.Heblo.Application.Features.Logistics.UseCases` (line 4)
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateResponse.cs` — namespace: `Anela.Heblo.Application.Features.Logistics.UseCases` (line 4)

The handler itself is correctly placed in `UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`, as are all other use cases (`AddItemToBox/`, `CreateNewTransportBox/`, `GetTransportBoxById/`, etc.). The request and response are the only files that did not move into the subfolder.

## Why it matters
`filesystem.md` specifies that complex features place each use case in its own folder containing Handler, Request, and Response. The inconsistency makes the folder harder to navigate and the namespace mismatch (`UseCases` vs `UseCases.ChangeTransportBoxState`) makes the handler's `using` reference confusing compared to every other handler in the module.

## Suggested fix
Move the two files into `UseCases/ChangeTransportBoxState/` and update their namespaces from `Anela.Heblo.Application.Features.Logistics.UseCases` to `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState`. No logic changes required — only file location and namespace declaration.

---
_Filed by daily arch-review routine on 2026-05-15._