# Design: Remove Unused `groupId` Parameter from `AcquireGraphTokenAsync`

## Component Design

### GraphService

**File:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

**Responsibility:** Acquires Microsoft Graph API access tokens using the configured credential chain. The method `AcquireGraphTokenAsync` is a private helper that performs token acquisition independently of any group context.

**Before:**
```csharp
private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
```

**After:**
```csharp
private async Task<string> AcquireGraphTokenAsync(CancellationToken cancellationToken)
```

The method body is unchanged. Only the unused `groupId` parameter is removed from the signature.

**Call site (line 119):**

Before:
```csharp
var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);
```

After:
```csharp
var graphToken = await AcquireGraphTokenAsync(cancellationToken);
```

**Interface impact:** None. `AcquireGraphTokenAsync` is `private` and does not appear in `IGraphService`. No other files require changes.

## Data Schemas

Not applicable. This change removes a dead parameter from a private method. No data schemas, API contracts, database schemas, or event payloads are affected.
