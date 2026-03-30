# Decision: Use Classes (not Records) for API DTOs

**Decision:** All MediatR request/response types exposed via API endpoints must be C# classes with properties, not records.

**Why:** OpenAPI generators (used for TypeScript client auto-generation on build) have issues with C# records — parameter order in constructors causes unreliable schema generation. This broke the frontend API client.

**How to apply:**
- `[Required]`, `[JsonPropertyName]` and other validation attributes on class properties
- Internal domain objects that are never exposed through the API can still use records
- Applies to all `*Request` and `*Response` types in `Application/Features/*/UseCases/`
