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

### task: update-call-site

**File:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

**What to do:**
Update the internal call site (approximately line 119) from:
```csharp
var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);
```
to:
```csharp
var graphToken = await AcquireGraphTokenAsync(cancellationToken);
```

Remove the `groupId` argument from the invocation. No other call sites exist — `AcquireGraphTokenAsync` is `private` and does not appear in `IGraphService`.

**Acceptance criteria:**
- The call site passes only `cancellationToken`.
- `dotnet build` passes with no errors.
- `dotnet format` produces no diff on the file.
