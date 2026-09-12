## Module
BackgroundJobs

## Finding
`RecurringJobConfiguration` (Domain entity) imports `System.ComponentModel.DataAnnotations` for two distinct purposes, both of which violate Clean Architecture:

**1. Redundant property attributes** (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`, lines 1–8, 11–34):
```csharp
[Required]
[MaxLength(100)]
public string JobName { get; private set; }
// … and five more decorated properties
```
The Persistence layer already declares identical constraints via EF Core Fluent API in `RecurringJobConfigurationConfiguration.cs` (lines 23–51): `.HasMaxLength(100).IsRequired()` for each property. EF Core uses the Fluent API configuration — the attributes on the domain entity are never read by EF or enforced at runtime; they are dead markup.

**2. Framework exception type for domain invariants** (lines 58, 60, 63, 65, 67, 90, 92, 95, 97, 110, 131, 133):
```csharp
throw new ValidationException("JobName is required");
```
`System.ComponentModel.DataAnnotations.ValidationException` is an ASP.NET/data-binding framework type. Using it in the domain layer couples the innermost ring to an outer-ring framework concern.

## Why it matters
**Clean Architecture rule**: the Domain layer must not depend on infrastructure or framework packages. Both usages bring `System.ComponentModel.DataAnnotations` into Domain — a framework namespace whose sole reason for existing is to support ASP.NET model validation and EF attribute-based mapping (neither of which is a domain concern).

Additionally, the attributes create silent redundancy: if a future developer updates a `[MaxLength]` on the domain entity, the actual database constraint (in the Fluent config) remains unchanged, creating a discrepancy with no compile error.

## Suggested fix
1. **Remove the `[Required]` and `[MaxLength]` attributes** from `RecurringJobConfiguration`. The Fluent API config in `RecurringJobConfigurationConfiguration` already owns those constraints and is the single source of truth.
2. **Replace `throw new ValidationException(...)` with `throw new ArgumentException(...)`** (or a domain-specific exception if one exists in `Anela.Heblo.Domain.Shared`). The guards are already argument-style checks — `ArgumentException` is a BCL type with no framework dependency.
3. Remove the `using System.ComponentModel.DataAnnotations;` import from the domain entity entirely.

---
_Filed by daily arch-review routine on 2026-09-09._
