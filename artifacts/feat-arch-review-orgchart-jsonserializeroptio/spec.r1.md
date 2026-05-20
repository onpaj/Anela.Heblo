# Specification: Reuse `JsonSerializerOptions` in OrgChartService

## Summary
Extract the per-call `JsonSerializerOptions` instance in `OrgChartService.GetOrganizationStructureAsync` to a `private static readonly` field so the instance (and its internal reflection metadata cache) is created once and reused across all requests. This aligns with Microsoft's official `System.Text.Json` guidance and removes an unnecessary allocation hotspot.

## Background
`Backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartService.cs` (lines 39–42) currently constructs a new `JsonSerializerOptions` on every invocation of `GetOrganizationStructureAsync`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, options);
```

`JsonSerializerOptions` is documented as expensive to construct because it lazily builds reflection metadata caches keyed to the configured options. Microsoft explicitly recommends caching a single instance and reusing it. Although the org chart endpoint is currently cached for 30 minutes on the frontend (limiting blast radius), the pattern itself is incorrect and risks being copy-pasted into hotter code paths. Filed by the daily arch-review routine on 2026-05-19.

## Functional Requirements

### FR-1: Extract `JsonSerializerOptions` to a static readonly field
The per-call `JsonSerializerOptions` instance in `OrgChartService` must be replaced with a single `private static readonly` field, initialized once at type-load time with `PropertyNameCaseInsensitive = true`. The existing `JsonSerializer.Deserialize<OrgChartResponse>(content, ...)` call must use this shared field instead.

**Acceptance criteria:**
- `OrgChartService.cs` declares `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };` (or equivalent).
- `GetOrganizationStructureAsync` no longer constructs a local `JsonSerializerOptions` instance.
- The `Deserialize<OrgChartResponse>` call references the shared static field.
- No other behavior, signature, return value, or error handling in the method changes.
- `dotnet build` succeeds with no new warnings.
- `dotnet format` reports no changes required.

### FR-2: Preserve existing deserialization behavior
The new shared options instance must be semantically identical to the previous per-call instance: case-insensitive property matching enabled, every other option at its default. The deserializer must continue to produce the same `OrgChartResponse` for the same input.

**Acceptance criteria:**
- The only option configured on the static instance is `PropertyNameCaseInsensitive = true`.
- Existing unit/integration tests covering `OrgChartService.GetOrganizationStructureAsync` continue to pass without modification.
- The endpoint that surfaces this data returns identical responses to representative inputs before and after the change.

## Non-Functional Requirements

### NFR-1: Performance
A single `JsonSerializerOptions` instance is allocated per process lifetime instead of per request, eliminating the per-call allocation and avoiding rebuilding internal reflection metadata caches. No measurable regression to latency or throughput is acceptable; an improvement, if any, is incidental and not required to be measured.

### NFR-2: Security
No auth, data-sensitivity, or trust-boundary changes. The deserializer continues to operate on payloads already fetched by the existing HTTP client; no new inputs cross a trust boundary.

### NFR-3: Maintainability
The shared field must be `private` (scoped to `OrgChartService` only) and `readonly` so it cannot be mutated after initialization. This prevents accidental cross-call configuration drift and matches the idiom recommended by Microsoft's `System.Text.Json` documentation.

## Data Model
No data model changes. `OrgChartResponse` and any nested DTOs are untouched.

## API / Interface Design
No public API, controller, MediatR handler, or interface changes. The modification is internal to `OrgChartService` and invisible to callers.

## Dependencies
- `System.Text.Json` (already referenced).
- `Backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartService.cs` is the only file expected to change.

## Out of Scope
- Auditing or refactoring other call sites that construct `JsonSerializerOptions` per call elsewhere in the codebase.
- Changing the frontend `staleTime` or any caching behavior.
- Introducing a centralized/shared `JsonSerializerOptions` registry across modules.
- Adding new serializer options (e.g., naming policies, converters, source generation).
- Performance benchmarking or instrumentation.
- Changes to `OrgChartResponse` or any other DTO.

## Open Questions
None.

## Status: COMPLETE