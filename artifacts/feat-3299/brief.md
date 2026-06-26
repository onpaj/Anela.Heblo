## Module
UserManagement

## Finding
`GraphService.AcquireGraphTokenAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`, line 86) accepts a `string groupId` parameter that is never referenced in the method body:

```csharp
private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
{
    var scope = "https://graph.microsoft.com/.default";
    _logger.LogInformation("Attempting to acquire MS Graph token with application scope: {Scope}", scope);
    var graphToken = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
    // ... groupId never appears below this point
    return graphToken;
}
```

The parameter appears in neither the token acquisition call nor any log statement. It was presumably added for logging context but was never actually used.

## Why it matters
Dead parameters inflate the call site signature and mislead readers into thinking `groupId` influences token acquisition. A method named `AcquireGraphTokenAsync` should acquire a token — the group ID is irrelevant to that concern.

## Suggested fix
Remove `string groupId` from the parameter list of `AcquireGraphTokenAsync` and update the single call site at line 119:
```csharp
// Before
var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);

// After
var graphToken = await AcquireGraphTokenAsync(cancellationToken);
```

---
_Filed by daily arch-review routine on 2026-06-22._
