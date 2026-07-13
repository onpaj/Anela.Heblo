# [arch-review] Journal: GetJournalIndicatorsAsync and JournalIndicatorDto are dead code

## Module
Journal

## Finding
`IJournalRepository.GetJournalIndicatorsAsync` is declared in the repository interface but has zero callers anywhere in the application or API layer:

- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:31–34` — method on the interface
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs:154–202` — 49-line implementation (the most complex method in the repository)
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs:1–12` — DTO that would wrap the result, also unused

A grep for `GetJournalIndicatorsAsync` across `backend/src/` returns only the definition, the interface, and the implementation — no handler or service calls it.

## Why it matters
The unused method bloats the `IJournalRepository` interface: every mock in unit tests must account for it, and future maintainers must read (and reason about) 49 lines of non-trivial aggregation logic that is never exercised. Violates YAGNI. The `JournalIndicatorDto` in `Contracts/` also occupies OpenAPI surface area it will never actually fill.

## Suggested fix
Remove `GetJournalIndicatorsAsync` from `IJournalRepository`, delete the implementation in `JournalRepository.cs` (lines 154–202), and delete `JournalIndicatorDto.cs`. If the feature is needed in the future, it can be re-added when there is a concrete consumer.

---
_Filed by daily arch-review routine on 2026-07-09._
