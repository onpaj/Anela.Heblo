## Module
Logistics

## Finding
`LogisticsController` contains GUID parsing logic and a hard-coded fallback system-user GUID, repeated identically in two action methods:

- `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs`, lines 80–93 (`CreateGiftPackageManufacture`):
  ```csharp
  var currentUser = _currentUserService.GetCurrentUser();
  if (!Guid.TryParse(currentUser.Id, out var userId))
  {
      userId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // System/Mock user GUID
  }
  request.UserId = userId;
  ```
- Lines 104–116 (`DisassembleGiftPackage`): identical block.

The controller also injects `ICurrentUserService` solely to serve this logic. All other action methods in the controller pass requests directly to `_mediator.Send()` without touching user identity.

## Why it matters
`development_guidelines.md` forbids "Business logic in Controller class." The mock-auth fallback and the decision of what constitutes a valid user ID are application concerns. Placing them in the controller hides the rule from the handler's unit tests and duplicates it — if the fallback GUID ever changes, both locations must be updated.

## Suggested fix
Move the resolution into the two handlers (`CreateGiftPackageManufactureHandler`, `DisassembleGiftPackageHandler`) or into a shared helper injected by the handler. The controller action becomes a one-liner:

```csharp
[HttpPost("gift-packages/manufacture")]
public async Task<ActionResult<CreateGiftPackageManufactureResponse>> CreateGiftPackageManufacture(
    [FromBody] CreateGiftPackageManufactureRequest request,
    CancellationToken cancellationToken)
{
    return HandleResponse(await _mediator.Send(request, cancellationToken));
}
```

Remove `ICurrentUserService` from the controller; inject it only in the handlers that need it.

---
_Filed by daily arch-review routine on 2026-05-15._