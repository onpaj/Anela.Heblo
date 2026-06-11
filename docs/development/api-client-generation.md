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
// Also wrong: uses private fields of the generated client — breaks silently on NSwag regeneration
```

### The Solution

Always use the configured API client through our helper functions.

**✅ CORRECT — for standard hooks (the default pattern):**
```typescript
import { getAuthenticatedApiClient } from './client';

const client = getAuthenticatedApiClient();
const result = await client.catalog_GetList({ searchTerm: '', pageNumber: 1, pageSize: 20 });
```

> **❌ AVOID**: `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`
> These reach into private fields of the NSwag-generated class. If NSwag renames those fields,
> the code breaks at runtime with no compile-time warning.
> Use `getApiBaseUrl()` and `getAuthenticatedFetch()` from `./client` instead.

**✅ CORRECT — for endpoints whose business outcomes are surfaced as HTTP status codes (e.g. 409 Conflict):**

The preferred pattern is to model the business outcome in the OpenAPI contract and let the generated client surface it as a typed, non-throwing branch. Annotate the controller action with both the success and the business-outcome status — both pointing at the same response DTO — so that the NSwag template override (when active) emits a typed `else if (status === 4xx)` branch. Until the template is activated, use a hook-level `try/catch` to handle the typed exception:

```csharp
[ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status409Conflict)]
[HttpPost("{id:guid}/feedback")]
public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(...) { ... }
```

Then call the typed method and discriminate on the exception status or the existing `BaseResponse.success` + `errorCode` envelope:

```typescript
import { getAuthenticatedApiClient } from './client';
import { SubmitArticleFeedbackRequest } from './generated/api-client';

const client = getAuthenticatedApiClient();
const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });

try {
  const response = await client.articles_SubmitFeedback(articleId, request);
  return { precisionScore: response.precisionScore, styleScore: response.styleScore };
} catch (e: unknown) {
  const err = e as { status?: number };
  if (err.status === 409) {
    // 409 path — already submitted
    return { alreadySubmitted: true };
  }
  throw e;
}
```

See `useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts` for the canonical example, and `backend/src/Anela.Heblo.API/nswag-templates/README.md` for the template-override status.

**Escape hatch — `getApiBaseUrl()` + `getAuthenticatedFetch()`.**

Reach for these helpers only when an endpoint's business outcome cannot yet be expressed through the generated client — for example, an `If-Match`-based update returning HTTP 412 Precondition Failed before the controller has been annotated with `[ProducesResponseType(StatusCodes.Status412PreconditionFailed)]`. The helpers attach auth headers, do not throw on non-2xx, and do not trigger the global error toast — leaving status-code branching entirely to the caller:

```typescript
import { getApiBaseUrl, getAuthenticatedFetch } from './client';

const url = `${getApiBaseUrl()}/api/resources/${id}`;
const response = await getAuthenticatedFetch()(url, {
  method: 'PUT',
  headers: { 'If-Match': etag, 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});
if (response.status === 412) return { precondition: 'stale' };
if (!response.ok) throw new Error(`HTTP ${response.status}`);
return response.json();
```

### Enforcement Rules

1. **NEVER use relative URLs** directly in `fetch` calls within API hooks
2. **ALWAYS use `getAuthenticatedApiClient()`** for standard typed calls, or `getApiBaseUrl()` + `getAuthenticatedFetch()` when you need to branch on HTTP status codes
3. **NEVER use `(apiClient as any)`** to access private fields — use public helper functions instead
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
import { getApiBaseUrl, getAuthenticatedFetch } from './client';

const url = `${getApiBaseUrl()}/api/catalog`;
const response = await getAuthenticatedFetch()(url, { method: 'GET' });
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
