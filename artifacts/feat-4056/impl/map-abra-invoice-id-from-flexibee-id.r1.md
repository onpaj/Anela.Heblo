# Implementation: map-abra-invoice-id-from-flexibee-id

## What was implemented
Added a `.ForMember(...)` mapping to `FlexiReceivedInvoiceMappingProfile` so that `ReceivedInvoiceFlexiDto.Id` (FlexiBee's internal record identifier, an `Int32`) is mapped to `ReceivedInvoice.AbraInvoiceId` (stringified via `CultureInfo.InvariantCulture`). This profile is used by both `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync`, so `AbraInvoiceId` is now populated everywhere a `ReceivedInvoice` originates from FlexiBee.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs` — added `using System.Globalization;` and a new `.ForMember(dest => dest.AbraInvoiceId, opt => opt.MapFrom(src => src.Id.ToString(CultureInfo.InvariantCulture)))` mapping, placed immediately before the existing `InvoiceNumber` mapping.

## Tests
No new tests added — build/regression check only, per task instructions (there is no existing unit test file for this mapping profile; the change is verified by a full project build validating the AutoMapper expression trees compile, plus the existing test suite to confirm no regression).

## How to verify
1. `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` — succeeded, 0 errors (148 pre-existing warnings, unrelated to this change).
2. `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj` — result: Failed: 72, Passed: 270, Skipped: 5, Total: 347. Verified by stashing the change and re-running: the exact same 72/270/5/347 split occurs on the base branch, confirming these are pre-existing integration-test failures (they require a live FlexiBee connection, e.g. `FlexiIntegrationTestFixture` throwing `ArgumentNullException` for `implementationInstance` when FlexiBee config/credentials are absent in this environment) and not a regression introduced by this change.

## Notes
The file's starting content matched the task's expected "before" state exactly, so the diff was applied as specified with no adaptation needed.

## PR Summary
This change adds the missing mapping of FlexiBee's internal integer record id (`ReceivedInvoiceFlexiDto.Id`) to the domain's `ReceivedInvoice.AbraInvoiceId` property inside `FlexiReceivedInvoiceMappingProfile`. Because both `GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync` on `FlexiReceivedInvoicesClient` route through this single AutoMapper profile, the one-line addition (plus a `using System.Globalization;`) ensures `AbraInvoiceId` is populated consistently everywhere a `ReceivedInvoice` is built from FlexiBee data, without touching any other mapping or call site.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs` — map `src.Id` (invariant-culture stringified) to `dest.AbraInvoiceId`, placed next to the `InvoiceNumber` mapping.

## Status
DONE
