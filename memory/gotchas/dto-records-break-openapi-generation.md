# Gotcha: C# Records Break OpenAPI Client Generation

**Problem:** Using C# records for API request/response DTOs causes the OpenAPI generator to produce incorrect TypeScript client code — parameter order in the generated constructors is unreliable.

**Root cause:** OpenAPI generators treat record primary constructors differently from class properties. The schema generation is not deterministic with records.

**Fix:** Always use classes with properties for any DTO that flows through the API:
```csharp
// Wrong:
public record GetCatalogRequest(string Code, int Page);

// Correct:
public class GetCatalogRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
    public int Page { get; set; }
}
```

Internal types (never serialized to/from API) can still use records.
