# Architecture assessment: fold Logistics→Purchase into the shared `Rules()` theory

## Verdict

**Approved as designed.** This is a subtractive, test-infrastructure-only change. I
verified every factual claim in `design-01.md` and `plan-01.md` directly against the
current state of
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (1071 lines,
read in full this session) rather than trusting the prior steps' line numbers. All
claims hold; no invariant in this repo is at risk.

## Alignment with existing patterns and integration points

- **The file's own dominant pattern is the target shape.** 26 of 27 rows already
  wired through `Rules()`/`TheoryData` are executed by the single
  `Consumer_types_should_not_reference_provider_owned_namespaces` theory
  (lines 699–743). The Logistics→Purchase Fact (745–813) is the sole outlier. Folding
  it in doesn't introduce a new pattern — it removes the last exception to one.
- **Empty-allowlist convention confirmed.** Design proposes an inline
  `new HashSet<string>(StringComparer.Ordinal)` rather than a named field. I counted
  8 existing rows doing exactly this today (lines 423, 456, 467, 478, 490, 501, 513,
  524, 536, 678 — PackingMaterials→Invoices, ExpeditionListArchive→ExpeditionList,
  both Analytics→Catalog/Invoices/Bank pairs, Bank→Analytics, FinancialOverview→Catalog).
  Every *named* allowlist field in the file (`LeafletAllowlist`,
  `LogisticsAllowlist`, `CatalogPurchaseAllowlist`, etc.) holds at least one
  justified entry with a comment. An empty, entry-less allowlist for
  Logistics→Purchase should not get a name — the design matches the file's real
  convention, not just a plausible one.
- **`InspectedAssembly` default is correct.** `ModuleBoundaryRule` defaults
  `InspectedAssembly` to `"Anela.Heblo.Application"` (record declaration, line 20).
  The deleted Fact hard-codes `Assembly.Load("Anela.Heblo.Application")` (line 776).
  Omitting the parameter in the new row reproduces the same assembly scope — verified,
  not assumed.
- **Algorithmic equivalence is exact, not approximate.** I diffed `IsLogisticsForbidden`
  (759–774) against the shared `IsForbidden` (988–1003) character-by-character: same
  null-namespace short-circuit, same `Equals`/`StartsWith(prefix + ".")` ordinal
  check, same return semantics. The declaring-type fallback block in the Fact
  (797–803) is likewise identical in structure and intent to the theory's own
  (726–732). `EnumerateReferencedTypes` and `ExpandGenerics` (1014–1070) are shared
  infrastructure already, untouched by either path. There is no hidden behavioral
  divergence the design missed.
- **Documented project invariant.** `docs/architecture/development_guidelines.md`
  lists "Don't ignore module boundaries — Respect the architecture" as pitfall #7,
  and module boundaries are enforced project-wide via consumer-owned contracts. This
  test file *is* that enforcement mechanism. Consolidating it onto one code path
  strengthens rather than weakens that invariant — future hardening of the shared
  algorithm (as already happened once, per the design's citation of the
  declaring-type fallback being "added after the fact") now reaches all ~36 pairs
  uniformly instead of 35-of-36.

## Proposed architecture

No new component. This is deletion of ~65 duplicate lines (Fact body +
`IsLogisticsForbidden`) plus one `ModuleBoundaryRule` data row (~9 lines) into the
existing `TheoryData` collection. `ModuleBoundaryRule` (the record type) is reused
verbatim — no shape change. No production code, no new interface, no new test
infrastructure primitive. This is the correct level of intervention: the duplication
is precisely a missing data row, not a design gap, so the fix is data, not code.

## Implementation guidance

- **Where**: single file,
  `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.
- **Delete**: lines 745–813 in full (the `[Fact]` method, including its local
  `forbiddenPrefixes`, `logisticsAllowlist`, and nested `IsLogisticsForbidden`).
- **Add**: one `ModuleBoundaryRule` row to the `Rules()` initializer, per the design's
  snippet — placement next to the other Logistics-as-consumer rows ("Logistics ->
  Manufacture" at 403, "Logistics -> Catalog" at 436) is sensible for local
  readability; `TheoryData` order carries no semantic weight, confirmed by reading
  the theory (it iterates `consumerTypes` per-row, no cross-row state).
- **Don't touch**: `IsForbidden`, `EnumerateReferencedTypes`, `ExpandGenerics`, the
  theory method itself, or any other rule/allowlist in the file. Scope is exactly the
  one pair.
- **Data flow**: unchanged. `Rules()` → xUnit `[MemberData]` → one theory invocation
  per row → `Assembly.Load(rule.InspectedAssembly).GetTypes()` filtered by
  `InspectedNamespacePrefix` → `EnumerateReferencedTypes` per type →
  `IsForbidden`/allowlist check → assert no violations. The new row enters this exact
  pipeline; nothing bypasses or special-cases it.

## Risks and mitigations

- **Risk: the new theory row surfaces a real violation the duplicated (and possibly
  subtly different) old Fact missed.** Mitigated by the line-by-line equivalence
  check above — I found no algorithmic difference between `IsForbidden` and
  `IsLogisticsForbidden`, and both allowlists are empty, so there is no basis for the
  new row behaving differently against today's codebase. Residual risk is
  effectively zero, but the implementer must still run
  `dotnet test --filter FullyQualifiedName~ModuleBoundariesTests` to confirm rather
  than trust this analysis alone (plan-01.md FR-3 already requires this). If it does
  fail, plan-01.md correctly directs stopping and flagging rather than silently
  allowlisting — do not let this task quietly turn into a boundary-violation fix.
- **Risk: test count/name drift breaks CI reporting or coverage tooling that keys on
  the old Fact's name.** Low — nothing in the file or typical CI config references
  `Logistics_types_should_not_reference_Purchase_owned_namespaces` by name outside
  the file itself (single-file, self-contained test class, no `[MemberData]` reuse
  elsewhere). Plan-01.md step 6 already calls for a repo-wide grep for the name as a
  sanity check; keep that step.
- **No risk to production code** — the change is entirely inside
  `backend/test/`, and `ModuleBoundaryRule` is not consumed outside this file.

## Prerequisites before implementation begins

None. No open architectural question remains — the design's two "open questions"
(row placement, allowlist naming) are both non-load-bearing style choices already
resolved sensibly by the design and consistent with existing file conventions.
Implementation can proceed directly per plan-01.md's rough plan.
