## Module
OrgChart

## Finding
`OrgChartService.cs` lines 39–42 construct a new `JsonSerializerOptions` instance inside `GetOrganizationStructureAsync` on every invocation:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, options);
```

`JsonSerializerOptions` is expensive to create: on first use it builds reflection metadata caches internally. Microsoft's own documentation explicitly warns against constructing new instances per call and recommends reusing a single instance.

## Why it matters
Every org chart request allocates and immediately discards a `JsonSerializerOptions` instance along with its internal state. While a 30-minute `staleTime` on the frontend limits call frequency, the pattern is still wrong by default and sets a bad example for code that gets copied into more frequently called paths.

## Suggested fix
Extract to a `private static readonly` field:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```

Then use `JsonOptions` in the call to `JsonSerializer.Deserialize`. No other changes needed.

---
_Filed by daily arch-review routine on 2026-05-19._