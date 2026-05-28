## Module
Journal

## Finding
Two near-identical types represent the same concept — a per-product journal summary — in two different layers:

**Domain** (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs`):
```csharp
public class JournalIndicator
{
    public string ProductCode { get; set; } = null!;
    public int DirectEntries { get; set; }
    public int TotalEntries => DirectEntries;   // no actual computation
    public DateTime? LastEntryDate { get; set; }
    public bool HasRecentEntries { get; set; }
}
```

**Application** (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`):
```csharp
public class JournalIndicatorDto
{
    public string ProductCode { get; set; } = null!;
    public int DirectEntries { get; set; }
    public int TotalEntries => DirectEntries;   // identical no-op
    public DateTime? LastEntryDate { get; set; }
    public bool HasRecentEntries { get; set; }
}
```

`JournalIndicator` is not a domain entity: it has no identity, no lifecycle, no behaviour, and enforces no invariants. It is a **read model / query projection** produced by `IJournalRepository.GetJournalIndicatorsAsync`. Read models belong in the Application layer (Contracts folder), not Domain.

Additionally, `TotalEntries => DirectEntries` is a no-op computed property on both types — it adds no value over referencing `DirectEntries` directly. It is YAGNI.

## Why it matters
- The Domain layer accumulates types that are not domain objects, making the domain model harder to understand.
- The two types must be kept in sync manually; if one gains a new field, the other must too. This has already happened with the `// Within last 30 days` comment appearing identically in both.
- Per `docs/architecture/development_guidelines.md`, the Domain layer should contain entities, aggregates, value objects, and repository interfaces — not query projections.

## Suggested fix
1. Delete `JournalIndicator` from `Anela.Heblo.Domain.Features.Journal`.
2. Update `IJournalRepository.GetJournalIndicatorsAsync` to return `Dictionary<string, JournalIndicatorDto>` (Application type) — or define a minimal interface type in Domain if the repo interface must remain purely domain-typed (e.g. a simple `record struct JournalCount(int Direct, DateTime? LastDate)`).
3. Remove `TotalEntries => DirectEntries` from `JournalIndicatorDto` — callers can use `DirectEntries` directly.

---
_Filed by daily arch-review routine on 2026-05-27._