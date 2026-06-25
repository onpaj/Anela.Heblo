### task: remove-groupid-parameter-from-signature

**File:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

**What to do:**
Change the method signature of `AcquireGraphTokenAsync` from:
```csharp
private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
```
to:
```csharp
private async Task<string> AcquireGraphTokenAsync(CancellationToken cancellationToken)
```

Remove the `string groupId` parameter. Do not touch the method body — `groupId` is not referenced there, so no further changes are needed inside the method.

**Acceptance criteria:**
- The method signature contains only `CancellationToken cancellationToken`.
- No `groupId` parameter or variable remains in the method declaration.
- `dotnet build` passes with no errors.

---

