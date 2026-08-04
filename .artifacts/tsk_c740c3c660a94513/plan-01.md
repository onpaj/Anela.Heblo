# Plan — Flexi: `ILotsClient` registered twice with conflicting lifetimes

## Summary

`FlexiAdapterServiceCollectionExtensions.AddFlexiAdapter` registers `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient → FlexiLotsClient` twice — once as Singleton (line 73) and once as Scoped (line 86, via the unqualified `ILotsClient` bound by the `using` on line 23). This is a duplicate-registration defect with no intentional purpose; the fix is to delete the redundant Scoped registration and keep a single, verified lifetime.

## Context

This was flagged by an architecture review (see evidence in the task). The project's DI guidelines (`docs/architecture/development_guidelines.md` § Dependency Injection Patterns) expect one explicit binding per service per module registration extension. Two descriptors for the same service/implementation pair is undefined-behavior-adjacent: direct resolution silently picks the last registration (Scoped), while `IEnumerable<ILotsClient>` resolution yields two instances with different lifetimes — a bug class the guidelines already call out via the prior `IDqtRunRepository` incident (ADR-004 discussion).

## Investigation already performed (do not re-derive)

- Confirmed both lines 73 and 86 bind the exact same Domain interface (`Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient`) to the same implementation (`FlexiLotsClient`) — the SDK's identically-named `Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate.ILotsClient` is a distinct type never imported into this file, so there's no ambiguity about "two different services with the same short name."
- Probed the FlexiBee SDK's own `AddFlexiBee(services, configuration)` (`Rem.FlexiBeeSDK.Client` v0.1.139) by driving it against a minimal `IServiceCollection` in an isolated console harness. Result: the SDK registers its own `ILotsClient → LotsClient` as **Singleton**. This means `FlexiLotsClient` (which only wraps that SDK client, no other scoped dependency) has **no captive-dependency risk** at either lifetime, but Singleton is the natural, already-compatible choice.
- Checked the sibling registrations in the same file: the large majority of comparable Flexi read-client wrappers (`IErpStockClient`, `ICatalogSalesClient`, `IConsumedMaterialsClient`, `IPurchaseHistoryClient`, `IManufactureHistoryClient`, `ISupplierRepository`, `ICatalogAttributesClient`) are all `AddSingleton`. Singleton is the established pattern for this class of stateless read-only Flexi client wrapper in this file — Scoped is the outlier here, not Singleton.
- Checked all consumers of the Domain `ILotsClient` (`CatalogDataRefreshService`, `FlexiLotLoader`, plus adapter/unit tests) — none inject anything into `FlexiLotsClient` or its consumers that would require per-request scoping; `FlexiLotsClient` itself is stateless.

**Conclusion: keep the Singleton registration (line 73), delete the Scoped registration (line 86).** This matches the module's existing convention, is compatible with the SDK's own Singleton lifetime for the wrapped client, and requires no consumer changes.

## Functional requirements

- **FR-1**: `FlexiAdapterServiceCollectionExtensions.AddFlexiAdapter` must register `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient → FlexiLotsClient` exactly once, as `Singleton`.
  - Acceptance: after the change, `services.Where(d => d.ServiceType == typeof(ILotsClient))` (domain type) yields exactly one `ServiceDescriptor`, with `Lifetime == ServiceLifetime.Singleton` and `ImplementationType == typeof(FlexiLotsClient)`.
- **FR-2**: No behavioral change to any consumer (`CatalogDataRefreshService`, `FlexiLotLoader`) — they continue to resolve `ILotsClient` successfully with the same effective instance-sharing semantics as before the fix (previously they transparently got the last-registered Scoped instance; after the fix they get the Singleton instance, which is safe per the investigation above).

## Non-functional requirements

- No performance or security impact expected; this is a DI wiring correctness fix. Confirm the app still starts (host builds/validates DI graph) after the change.

## Data model

N/A — no data model impact.

## Interfaces

N/A — no public API, contract, or event impact. Purely an internal DI composition-root change in `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`.

## Dependencies and scope

**In scope:**
- Delete line 86 (`services.AddScoped<ILotsClient, FlexiLotsClient>();`) from `FlexiAdapterServiceCollectionExtensions.cs`.
- Keep line 73 (`services.AddSingleton<Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient, FlexiLotsClient>();`) as the sole registration — optionally simplify to `services.AddSingleton<ILotsClient, FlexiLotsClient>();` since the `using Anela.Heblo.Domain.Features.Catalog.Lots;` on line 23 already makes the fully-qualified form on line 73 redundant (cosmetic; not required for correctness, keep or drop per reviewer taste, but do not touch unrelated lines).
- Verify no other file in the Flexi adapter or elsewhere depends on `ILotsClient` being Scoped (already checked — none do).

**Out of scope:**
- The other arch-review findings from the same batch (Photobank, Manufacture, ApiClient, ProductMargins, MCP) — separate tasks, not touched here.
- Any change to the FlexiBee SDK package itself.
- Broader DI audit of the rest of `FlexiAdapterServiceCollectionExtensions.cs` beyond this one duplicate.

## Rough plan

1. Remove the duplicate `services.AddScoped<ILotsClient, FlexiLotsClient>();` on line 86 of `FlexiAdapterServiceCollectionExtensions.cs`.
2. Leave the Singleton registration on line 73 as the single source of truth (fully-qualified name can stay as-is or be simplified to the short `ILotsClient` — either is fine given the existing `using`).
3. Build the backend (`dotnet build`) to confirm no compile errors from the removed line.
4. Run the existing Flexi adapter test suite, specifically `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/FlexiLotsClientTests.cs` and any DI-composition/startup test, to confirm nothing depended on the Scoped registration.
5. Optionally add/confirm a lightweight DI-registration test asserting `ILotsClient` resolves to exactly one Singleton descriptor, to guard against regression of this exact defect class (mirrors the guideline's concern about duplicate registrations recurring, e.g. `IDqtRunRepository`).
6. Run `dotnet format` per repo validation checklist.

## Open questions

- None blocking. One minor style call left to the implementer: whether to simplify line 73 from the fully-qualified `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient` to the short `ILotsClient` — default: leave as-is (fully qualified) to keep the diff minimal and unambiguous about which `ILotsClient` is intended, given the SDK has a same-named type in scope elsewhere in the codebase.
