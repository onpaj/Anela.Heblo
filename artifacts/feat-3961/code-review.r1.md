## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs:107` — the two-entry `guidMap` (`KnownGuidPpl` → `PPL`, `KnownGuidZasilkovna` → `Zasilkovna`) is built identically in both `Map_ReturnsConfiguredMethod_WhenGuidIsKnown` (line 107) and `Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithNonEmptyMap` (line 128). Extracting it to a `private static readonly Dictionary<string, ShippingMethod>` field would remove the duplication; purely cosmetic, doesn't affect correctness.
