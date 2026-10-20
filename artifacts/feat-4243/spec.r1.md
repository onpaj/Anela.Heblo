# Specification: Use ConfigurationConstants defaults in ApplicationConfiguration.CreateWithDefaults

## Summary
`ApplicationConfiguration.CreateWithDefaults` hardcodes the fallback literals `"1.0.0"` and `"Production"` instead of referencing `ConfigurationConstants.DEFAULT_VERSION` and `ConfigurationConstants.DEFAULT_ENVIRONMENT`, which already define these same values in the same namespace. This is a one-line-per-fallback DRY fix: swap the two literals for the existing constants, with no behavioral change.

## Background
`ConfigurationConstants` (in `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`) exists specifically to be the single source of truth for configuration default values, including `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`. `ApplicationConfiguration.CreateWithDefaults` (in the same file's sibling, `ApplicationConfiguration.cs`) independently hardcodes the identical string literals rather than referencing the constants. The values currently match, so there is no observable bug today, but the duplication means a future change to `ConfigurationConstants` would silently fail to propagate to the factory method, since nothing ties the two together. This was flagged by an architecture review as a DRY violation within the Domain layer.

## Functional Requirements

### FR-1: Reference `ConfigurationConstants` in `CreateWithDefaults`
Replace the hardcoded string literals `"1.0.0"` and `"Production"` in `ApplicationConfiguration.CreateWithDefaults` (lines 24–27 of `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`) with `ConfigurationConstants.DEFAULT_VERSION` and `ConfigurationConstants.DEFAULT_ENVIRONMENT` respectively.

**Acceptance criteria:**
- `CreateWithDefaults` contains no hardcoded `"1.0.0"` or `"Production"` literals; it references `ConfigurationConstants.DEFAULT_VERSION` and `ConfigurationConstants.DEFAULT_ENVIRONMENT` instead.
- Calling `CreateWithDefaults(null, null, false)` returns an `ApplicationConfiguration` with `Version == ConfigurationConstants.DEFAULT_VERSION` ("1.0.0") and `Environment == ConfigurationConstants.DEFAULT_ENVIRONMENT` ("Production") — i.e., observable behavior is unchanged.
- Calling `CreateWithDefaults` with non-null `version`/`environment` values still passes those values through unchanged (the `??` fallback logic is untouched).
- No other members, files, or call sites are modified.
- `dotnet build` and `dotnet format` succeed with no new warnings introduced by this change.
- Any existing unit tests covering `ApplicationConfiguration.CreateWithDefaults` continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time constant substitution with no runtime performance impact.

### NFR-2: Security
Not applicable — no security-sensitive data, auth, or external input is involved.

## Data Model
No data model changes. `ApplicationConfiguration` remains a Domain-layer class with `Version` (string), `Environment` (string), and `UseMockAuth` (bool) properties, unchanged.

## API / Interface Design
No public API, contract, or signature changes. `CreateWithDefaults(string? version, string? environment, bool useMockAuth)` keeps its existing signature and return type; only the internal fallback expressions change from literals to constant references.

## Dependencies
None. Both `ApplicationConfiguration` and `ConfigurationConstants` already exist in the same namespace (`Anela.Heblo.Domain.Features.Configuration`) in the same file directory, so no new using directives or project references are needed.

## Out of Scope
- Any change to the values of `ConfigurationConstants.DEFAULT_VERSION` or `ConfigurationConstants.DEFAULT_ENVIRONMENT`.
- Any change to `ApplicationConfiguration`'s constructor, properties, or null-checking behavior.
- Any change to callers of `CreateWithDefaults` (e.g., wherever version/environment are read from configuration/environment variables at startup).
- Any broader audit of other duplicated literals elsewhere in the Configuration module or codebase.

## Open Questions
None.

## Status: COMPLETE
