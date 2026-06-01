# OpenAPI Client Generation

This document describes how OpenAPI clients are generated for both backend (C#) and frontend (TypeScript) in the Anela Heblo project.

## Overview

The project uses **NSwag** to automatically generate API clients from the ASP.NET Core API OpenAPI specification. This ensures type-safe API communication and reduces manual coding errors.

**Two clients are generated:**
1. **Backend C# Client** - For internal backend testing and server-to-server communication
2. **Frontend TypeScript Client** - For React frontend to communicate with the API

## Backend C# Client

### Location
- **Project**: `backend/src/Anela.Heblo.API.Client/`
- **Generated file**: `Generated/AnelaHebloApiClient.cs`

### Auto-Generation

The C# client is automatically generated via a **PostBuild event** in the API project.

**When it runs:**
- **Debug mode only** - Automatically generated after building `Anela.Heblo.API` in Debug configuration
- **Not in Release mode** - Skips generation in production builds for faster builds

**Build configuration:**
```xml
<Target Name="GenerateApiClient" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
  <Exec Command="dotnet tool restore" />
  <Exec Command="dotnet nswag swagger2csclient ..." />
</Target>
```

### Manual Generation

```bash
# From repository root
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateBackendClient
```

### Configuration

**Tool**: NSwag with System.Text.Json serialization

**Settings:**
- Namespace: `Anela.Heblo.API.Client`
- JSON serialization: `System.Text.Json` (not Newtonsoft.Json)
- Generated classes: Client classes for all API controllers
- HTTP client: Uses `HttpClient` dependency injection

### Usage Example

```csharp
using Anela.Heblo.API.Client;

public class MyService
{
    private readonly AnelaHebloApiClient _apiClient;

    public MyService(AnelaHebloApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<WeatherForecast[]> GetWeatherAsync()
    {
        return await _apiClient.WeatherForecastAsync();
    }
}
```

## Frontend TypeScript Client

### Location
- **Generated file**: `frontend/src/api/generated/api-client.ts`
- **React hooks**: `frontend/src/api/hooks.ts` (manually written wrappers)

### Auto-Generation

The TypeScript client is generated in **two ways**:

1. **PostBuild event** in backend API project (Debug mode only)
2. **Prebuild script** in frontend `package.json` before `npm start` or `npm run build`

**Frontend prebuild script:**
```json
{
  "scripts": {
    "prebuild": "npm run generate-client",
    "generate-client": "cd ../backend/src/Anela.Heblo.API && dotnet msbuild -t:GenerateFrontendClientManual"
  }
}
```

### Manual Generation

```bash
# From repository root
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual

# Or from frontend directory
cd frontend
npm run generate-client
```

### Configuration

**Tool**: NSwag with Fetch API template

**Settings:**
- Template: `Fetch` (uses browser's `fetch` API)
- Type generation: TypeScript with interfaces
- Module resolution: ES6 modules
- null/undefined handling: TypeScript strict null checks
- Date handling: ISO 8601 strings converted to `Date` objects

### Integration with React

The generated client is integrated with **TanStack Query** (React Query) for data fetching, caching, and state management.

**Example React hook:**

```typescript
// frontend/src/api/hooks.ts
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from './client';

export function useWeatherQuery() {
  return useQuery({
    queryKey: ['weather'],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return client.weatherForecast();
    },
  });
}
```

**Usage in components:**

```typescript
import { useWeatherQuery } from '../api/hooks';

const WeatherComponent = () => {
  const { data, isLoading, error } = useWeatherQuery();

  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;

  return (
    <div>
      {data?.map((forecast) => (
        <div key={forecast.date}>{forecast.summary}</div>
      ))}
    </div>
  );
};
```

## API Endpoint Pattern

All API endpoints follow the standard REST convention:

```
/api/{controller}/{action?}
```

**Examples:**
- `GET /api/weather/forecast` - Weather forecast data
- `GET /api/configuration` - Application configuration
- `GET /api/catalog` - Catalog items
- `POST /api/catalog` - Create catalog item
- `PUT /api/catalog/{id}` - Update catalog item
- `DELETE /api/catalog/{id}` - Delete catalog item

**Controllers:**
- One controller per feature: `{Feature}Controller.cs`
- Located in: `backend/src/Anela.Heblo.API/Controllers/`
- Route template: `[Route("api/[controller]")]`

## CRITICAL: URL Construction Rules

**MANDATORY**: All API hooks MUST use absolute URLs with `baseUrl` to avoid calling wrong endpoints.

### The Problem

When using relative URLs in `fetch` calls, the browser uses the **current page's origin**, which is the frontend dev server (port 3000), not the backend API server (port 5001).

**❌ WRONG - Relative URL:**
```typescript
const url = `/api/catalog`;
const response = await (apiClient as any).http.fetch(url, {method: 'GET'});

// This calls: http://localhost:3000/api/catalog (WRONG!)
// Should call: http://localhost:5001/api/catalog
```

### The Solution

Always construct absolute URLs using the API client's `baseUrl`.

**✅ CORRECT - Absolute URL with baseUrl:**
```typescript
const relativeUrl = `/api/catalog`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, {method: 'GET'});

// This calls: http://localhost:5001/api/catalog (CORRECT!)
```

**✅ CORRECT - Alternative pattern (custom API client):**
```typescript
// Create custom API client class with makeRequest method
class CustomApiClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
  }

  async makeRequest<T>(url: string, options: RequestInit = {}): Promise<T> {
    const response = await fetch(`${this.baseUrl}${url}`, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
    });

    if (!response.ok) {
      throw new Error(`API error: ${response.statusText}`);
    }

    return response.json();
  }
}

// Usage
const client = new CustomApiClient('http://localhost:5001');
const data = await client.makeRequest('/api/catalog', {method: 'GET'});
```

### Enforcement Rules

1. **NEVER use relative URLs** directly in `fetch` calls within API hooks
2. **ALWAYS use `getAuthenticatedApiClient()`** to get the configured client
3. **ALWAYS construct absolute URLs** with `baseUrl` when making custom requests
4. **Verify base URL configuration** in environment-specific settings

### Base URL Configuration

**Development (`frontend/.env`):**
```bash
REACT_APP_API_BASE_URL=https://localhost:5001
```

**Staging:**
```bash
REACT_APP_API_BASE_URL=https://heblo.stg.anela.cz
```

**Production:**
```bash
REACT_APP_API_BASE_URL=https://heblo.anela.cz
```

## DTO Design Rules

### CRITICAL: Use Classes, Not Records

**MANDATORY**: All API request/response DTOs MUST be classes, not records.

**Why?**
- OpenAPI generators (NSwag) have issues with parameter order in C# records
- Records with positional parameters generate incorrect TypeScript interfaces
- Property detection is unreliable with records in OpenAPI schema

**❌ WRONG - Using records for API DTOs:**
```csharp
// DON'T use records for API DTOs
public record GetCatalogListRequest(string? SearchTerm, int PageNumber, int PageSize);
public record CatalogItemDto(string Code, string Name, string Type);
```

**✅ CORRECT - Using classes for API DTOs:**
```csharp
// Use classes for API DTOs
public class GetCatalogListRequest
{
    [Required]
    public string? SearchTerm { get; set; }

    [Required]
    public int PageNumber { get; set; } = 1;

    [Required]
    public int PageSize { get; set; } = 20;
}

public class CatalogItemDto
{
    [Required]
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
```

### Best Practices for DTOs

1. **Use classes** for all API request/response DTOs
2. **Add validation attributes**: `[Required]`, `[Range]`, `[MaxLength]`, etc.
3. **Use `[JsonPropertyName]`** if you need specific JSON property names
4. **Initialize properties** with default values where appropriate
5. **Keep DTOs in** `Application/Features/{Feature}/Contracts/` folder

### Internal Domain Objects

**Records are OK** for internal domain objects that are NOT exposed via API:

```csharp
// Internal domain objects can use records
public record DomainEvent(string EventType, DateTime OccurredAt, object Data);

// But API DTOs must use classes
public class EventDto
{
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public object Data { get; set; } = new();
}
```

## Regeneration Workflow

### When to Regenerate

Regenerate clients whenever:
1. **New API endpoints** are added
2. **Existing endpoints** are modified (parameters, return types)
3. **DTOs** are added or changed
4. **Routes** are updated

### Automatic Regeneration

**Backend builds (Debug mode):**
- Building `Anela.Heblo.API` in Debug mode automatically regenerates both clients
- Check build output for "Generating API clients..." messages

**Frontend builds:**
- `npm run build` runs prebuild script to regenerate TypeScript client
- `npm start` runs prebuild script before starting dev server

### Manual Regeneration

```bash
# Backend C# client
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateBackendClient

# Frontend TypeScript client
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual

# Or from frontend directory
cd frontend
npm run generate-client
```

### Verification

After regeneration, verify:

1. **No build errors** in generated files
2. **Correct TypeScript types** in `frontend/src/api/generated/api-client.ts`
3. **API hooks work** without type errors
4. **Frontend compiles** without errors: `npm run build`

## Troubleshooting

### Client Not Generated

**Check:**
1. API project builds successfully: `dotnet build backend/src/Anela.Heblo.API`
2. NSwag tool is installed: `dotnet tool restore`
3. OpenAPI spec is valid: Navigate to `https://localhost:5001/swagger/v1/swagger.json`

**Fix:**
```bash
# Restore tools
dotnet tool restore

# Clean and rebuild
dotnet clean
dotnet build backend/src/Anela.Heblo.API

# Manually regenerate
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

### TypeScript Type Errors

**Cause:** Generated types don't match API responses

**Fix:**
1. Ensure backend DTOs are **classes, not records**
2. Add proper validation attributes: `[Required]`, `[JsonPropertyName]`
3. Regenerate client: `npm run generate-client`
4. Check for breaking changes in API

### API Client URL Issues

**Symptom:** API calls fail with 404 or call wrong origin

**Fix:**
1. Verify `REACT_APP_API_BASE_URL` in `.env` file
2. Ensure API hooks use absolute URLs with `baseUrl`
3. Check network tab in browser DevTools to see actual request URLs

**Example fix:**
```typescript
// Before (WRONG)
const response = await fetch('/api/catalog');

// After (CORRECT)
const client = await getAuthenticatedApiClient();
const relativeUrl = '/api/catalog';
const fullUrl = `${(client as any).baseUrl}${relativeUrl}`;
const response = await fetch(fullUrl);
```

### Build Performance

**Issue:** Client generation slows down builds

**Solution:**
- Client generation only runs in **Debug mode**
- Release builds skip client generation
- Manually regenerate only when API changes

## Additional Resources

- **Setup & Commands**: `docs/development/setup.md`
- **Architecture**: `docs/architecture/filesystem.md`
- **Testing**: `docs/testing/playwright-e2e-testing.md`
- **NSwag Documentation**: https://github.com/RicoSuter/NSwag
