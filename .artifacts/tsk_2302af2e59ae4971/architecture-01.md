# Architecture review: split `IPhotobankRepository` into per-entity-family interfaces

## Verdict

**Amend before implementation.** The interface split itself (Domain layer, six files, method
groupings, consumer repointing table) is correct and verified directly against source — approved
as designed. But the **Persistence-layer / DI section of design-01.md violates an established,
consistently-applied codebase invariant**: in this repository, a repository interface maps 1:1 to
a repository *class*, and each class takes `ApplicationDbContext` directly in its constructor —
never one class implementing several narrow repository interfaces forwarded through a shared-
instance DI trick. Two other features in this codebase have the exact same shape as Photobank
(one module, several entity families) and both resolve it by splitting the physical class, not
just the interface. The design should do the same.

## What I verified against source

- `IPhotobankRepository.cs` (41 methods incl. `PhotoLocator` and `SaveChangesAsync`) — matches the
  design's method inventory exactly, including the two documented deviations
  (`GetPhotosByIdsAsync`, `RemovePhotoTagsBySourceAsync`) and the previously-unlisted
  `GetPhotoRuleCandidatesPageAsync`.
- `PhotobankRepository.cs` (Persistence, 446 lines): single class, single `ApplicationDbContext _context`
  field, **no cross-family private helper sharing** — `BuildFilterQuery` is Photo-only,
  `FindTagByNameAsync` is Tag-only. Cross-entity queries (e.g. `GetTagsWithCountsAsync` joining
  `PhotobankTags`/`PhotoTags`) go through `_context` directly, not through another repository's
  state. This confirms splitting the class is as mechanical as splitting the interface — nothing
  ties the six families together except the shared `DbContext`, which DI already shares correctly
  across any number of independently-constructed repository classes in the same scope.
- `PhotobankModule.cs`: current registration is `services.AddScoped<IPhotobankRepository, PhotobankRepository>();`
  — a single, plain line, consistent with every other module in the codebase.
- `docs/architecture/filesystem.md` (line 23, 141-144): documented convention is
  `I{Entity}Repository.cs` in Domain, `{Entity}Repository.cs` in Persistence — one repository
  interface, one entity family, one class.
- `docs/architecture/development_guidelines.md` (lines 118-148): the canonical DI example is
  `services.AddScoped<IOrderRepository, OrderRepository>();` — a direct, one-line binding per
  repository, owned by the feature's `{Feature}Module.cs`. No forwarding/shared-instance pattern
  appears anywhere in this guidance.
- **Direct precedent, same shape as Photobank** — a module with multiple entity families, each
  getting its own interface:
  - `Journal`: `IJournalRepository` (backed by `JournalRepository`, for `JournalEntry`) and
    `IJournalTagRepository` (backed by a **separate** `JournalTagRepository` class, for
    `JournalEntryTag`). `JournalModule.AddJournalModule()` registers both as two independent,
    plain `AddScoped<TInterface, TImpl>()` lines. No forwarding.
  - `PackingMaterials`: same shape — `IPackingMaterialRepository`/`PackingMaterialRepository` and
    `IPackingMaterialAllocationRepository`/`PackingMaterialAllocationRepository`, two classes, two
    plain registrations.
  - Grepped all 12 concrete repository classes under `backend/src/Anela.Heblo.Persistence/**`
    (`PurchaseOrderRepository`, `TransportBoxRepository`, `StockTakingRepository`,
    `GiftPackageManufactureRepository`, `DqtRunRepository`, `LotRepository`,
    `MaterialContainerRepository`, `ManufactureDifficultyRepository`,
    `StockUpOperationRepository`, `IssuedInvoiceRepository`, plus the two Journal/PackingMaterial
    pairs above): **every single one implements exactly one repository interface.** There is zero
    precedent anywhere in `Anela.Heblo.Persistence` for one repository class implementing more
    than one narrow repository interface.
  - `BaseRepository<TEntity, TKey>` (used by most of the above) independently implements
    `SaveChangesAsync` by calling `Context.SaveChangesAsync` — i.e. the existing convention already
    has *multiple classes, each exposing their own `SaveChangesAsync`, all committing through one
    shared scoped `DbContext`*. This is exactly FR-2's "duplicate `SaveChangesAsync` on every
    interface" decision — but the codebase already achieves it via separate classes sharing a
    constructor-injected `DbContext`, not via one class exposing it six times.
  - The `AddScoped<PhotobankRepository, ...>() → GetRequiredService<PhotobankRepository>()`
    forwarding pattern **does have precedent** in this codebase (`ShoptetOrderClient` implementing
    both `IEshopOrderClient` and `IShoptetExpeditionOrderSource`; similarly `AzureBlobPrintQueueSink`,
    `PlaudTokenRefreshClient`) — but every instance of it is an **external adapter/client** with two
    facade interfaces over one HTTP/SDK client, not an internal repository. It has never been used
    for a Persistence-layer repository, where the constructor-injected shared `DbContext` already
    solves the "same underlying state across multiple interfaces" problem for free.

## Why this matters

`ApplicationDbContext` is registered via `AddDbContext<ApplicationDbContext>()`
(`PersistenceModule.cs:99`), which is Scoped by default. Any number of repository classes
constructed in the same scope — each simply taking `ApplicationDbContext context` in its
constructor, exactly like every other repository in this codebase — automatically share the same
`DbContext` instance. That is *already* how "write via one repository, `SaveChangesAsync` via
another, and the write persists" works for Journal and PackingMaterials today. Nothing special is
required to get that guarantee for Photobank.

The design's shared-instance-forwarding registration reinvents that guarantee through an unfamiliar
mechanism, and — by the design document's own admission — is "the one part of this refactor that
isn't purely mechanical" and "must be implemented correctly on the first pass since a wrong
registration ... would silently produce data-consistency bugs that unit tests using mocks won't
catch" (design-01.md, DI wiring section; plan-01.md FR-5). That risk is self-inflicted: it exists
only because the design chose to keep one physical class. Splitting the class the same way the
interface is split removes the risk entirely — there is no longer a "forwarding registration to get
right," because each of the six classes is registered exactly like every other repository in the
codebase (`services.AddScoped<IPhotobankPhotoRepository, PhotobankPhotoRepository>();`, one line
per class), and DI's standard scoped-`DbContext`-sharing does the rest.

This also makes the refactor **more** mechanical, not less, relative to the design's own stated
goal ("pure declaration change — no persistence logic changes"): no method body changes, no query
changes — only moving each method (verbatim) into the file/class matching its interface, per the
same FR-1 grouping already agreed. The original finding's suggestion to keep one class was made
without checking this precedent; the design step inherited it uncritically instead of validating it
against the codebase's own pattern. I'm overriding that inherited scope decision here because it
conflicts with a consistently-applied invariant, not because the mechanical interface split itself
is wrong.

## Required amendments to design-01.md

1. **Persistence layer**: instead of one `PhotobankRepository : IPhotobankPhotoRepository, ...` (6
   interfaces), split into six concrete classes, one per interface, each in its own file under
   `backend/src/Anela.Heblo.Persistence/Photobank/`:
   - `PhotobankPhotoRepository : IPhotobankPhotoRepository`
   - `PhotobankTagRepository : IPhotobankTagRepository`
   - `PhotobankPhotoTagRepository : IPhotobankPhotoTagRepository`
   - `PhotobankRootRepository : IPhotobankRootRepository`
   - `PhotobankTagRuleRepository : IPhotobankTagRuleRepository`
   - `PhotobankAutoTagRepository : IPhotobankAutoTagRepository`

   Each class: `private readonly ApplicationDbContext _context;` + constructor taking
   `ApplicationDbContext context`, then the family's methods moved verbatim (cut, not rewritten)
   from the current `PhotobankRepository.cs`, plus its own `SaveChangesAsync` calling
   `_context.SaveChangesAsync(cancellationToken)`. Delete the old single-class file once all
   methods are relocated.

2. **DI wiring** (`PhotobankModule.AddPhotobankModule`): replace the shared-instance-forwarding
   block with six independent, plain registrations — the same style as every other module:
   ```csharp
   services.AddScoped<IPhotobankPhotoRepository, PhotobankPhotoRepository>();
   services.AddScoped<IPhotobankTagRepository, PhotobankTagRepository>();
   services.AddScoped<IPhotobankPhotoTagRepository, PhotobankPhotoTagRepository>();
   services.AddScoped<IPhotobankRootRepository, PhotobankRootRepository>();
   services.AddScoped<IPhotobankTagRuleRepository, PhotobankTagRuleRepository>();
   services.AddScoped<IPhotobankAutoTagRepository, PhotobankAutoTagRepository>();
   ```
   Drop the `GetRequiredService<PhotobankRepository>()` forwarding entirely — it no longer serves a
   purpose once there is no longer a single concrete class shared across interfaces.

3. **Verification plan**: drop the design's item 4 ("manual/one-time run to confirm shared
   instance") — it's now moot; there is no shared-instance invariant to prove, only the same
   scoped-`DbContext`-sharing every other multi-repository feature already relies on and already
   has test coverage proving works (via the Journal/PackingMaterial precedent, and via Photobank's
   own existing test suite, which already exercises multi-repository handlers like
   `RetagPhotosHandler` and will continue to after the mock types are split).

4. Everything else in design-01.md is unaffected: the six interfaces, their method groupings, the
   `PhotoLocator` relocation, the FR-2 decision to duplicate `SaveChangesAsync` per interface (now
   additionally justified by the `BaseRepository`/Journal precedent above), and the full
   consumer-repointing table all stand as designed.

## Minor note (non-blocking)

plan-01.md's summary prose says "16 UseCase handlers + 2 background jobs" (21 consumers total
including the DI module), but the actual FR-1/design-01.md consumer table lists 18 distinct
handlers (confirmed by grepping every file referencing `IPhotobankRepository` under
`backend/src/Anela.Heblo.Application/Features/Photobank/`: 18 handler files + 2 jobs + 1 module =
21). The table itself — which is what implementation should follow — is complete and accurate;
only the headline count in the plan's prose is off by two. Not worth revising the plan for, but
flagging so nobody re-derives "16" as a checklist target during implementation.

## Risks and mitigations

- **Risk**: moving 40+ method bodies across six new files by hand risks a copy-paste error (wrong
  file, dropped method, or a stray reference to a private helper that stayed behind in another
  class). **Mitigation**: `dotnet build` after the move will catch any missing method (interface
  not satisfied) or dangling reference (private helper not found) immediately — this is a
  compile-enforced move, not a silent-failure risk.
- **Risk**: test doubles for multi-family handlers (`ReapplyRulesHandlerTests`,
  `PhotobankIndexJobTests`, `PhotobankAutoTagJobTests`, `RetagPhotosHandlerTests`) need multiple
  `Mock<T>` fields instead of one — unchanged from the original design, still mechanical.
- **No new risk introduced by this amendment** — it removes the one risk the original design
  flagged as non-mechanical (FR-5) rather than adding one.

## Prerequisites before implementation begins

- None beyond what plan-01.md/design-01.md already established (no schema change, no API contract
  change, no external coordination). The amendment above is a same-PR correction to the
  Persistence/DI sections, not a separate workstream.
