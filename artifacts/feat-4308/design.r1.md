# Design: Remove out-of-scope Timestamp field from GetConfigurationResponse

## Component Design

No new components. The existing Configuration vertical slice keeps its current shape; only the shape of one DTO and one call site shrinks.

- **`GetConfigurationHandler`** (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`): unchanged responsibility (resolve version/environment/mock-auth from `IConfiguration` and assembly metadata). Its only change is to stop assigning a `Timestamp` value when constructing `GetConfigurationResponse`.
- **`GetConfigurationResponse`** (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`): unchanged responsibility (carry the three application-wide values back to the controller/client). Its contract narrows to exactly the three documented properties.
- **`ConfigurationController`**: no change — it is a pure MediatR pass-through and never references `Timestamp` directly.
- **`VersionService.checkVersion()`** (`frontend/src/services/versionService.ts`): unchanged responsibility (map the backend configuration response into the frontend's `VersionInfo` shape). Its `timestamp` field's value now always comes from the browser's own clock (`new Date().toISOString()`) instead of conditionally from the backend response.
- **`GetConfigurationEndpointTests`** and **`versionService.test.ts`**: test doubles/assertions updated to match the narrower contract (per arch-review FR-5/FR-6) — no new test components, just removal of assertions/fixture fields tied to the deleted `Timestamp`.

No new interfaces, no new dependency injection registrations, no new files.

## Data Schemas

### Backend response DTO — before
```csharp
public class GetConfigurationResponse : BaseResponse
{
    public string Version { get; set; } = default!;
    public string Environment { get; set; } = default!;
    public bool UseMockAuth { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### Backend response DTO — after
```csharp
public class GetConfigurationResponse : BaseResponse
{
    public string Version { get; set; } = default!;
    public string Environment { get; set; } = default!;
    public bool UseMockAuth { get; set; }
}
```

### Wire shape (`GET /api/configuration`) — before
```json
{
  "version": "1.2.3",
  "environment": "Production",
  "useMockAuth": false,
  "timestamp": "2026-09-25T10:00:00Z"
}
```

### Wire shape (`GET /api/configuration`) — after
```json
{
  "version": "1.2.3",
  "environment": "Production",
  "useMockAuth": false
}
```

### Generated TypeScript client type (`frontend/src/api/generated/api-client.ts`) — after regeneration
```typescript
export interface IGetConfigurationResponse {
    version?: string;
    environment?: string;
    useMockAuth?: boolean;
}

export class GetConfigurationResponse extends BaseResponse implements IGetConfigurationResponse {
    version?: string;
    environment?: string;
    useMockAuth?: boolean;
    // timestamp field and its init()/toJSON() handling removed by regeneration
}
```

### Frontend-local `VersionInfo` shape — unchanged
```typescript
interface VersionInfo {
  version: string;
  environment: string;
  useMockAuth: boolean;
  timestamp: string; // now always sourced from new Date().toISOString(), never from the backend
}
```

No database schema, migration, or persisted-entity changes — this DTO is constructed in-memory per request and never persisted.
