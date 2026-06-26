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
