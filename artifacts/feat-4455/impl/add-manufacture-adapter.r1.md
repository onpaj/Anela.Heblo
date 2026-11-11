# Impl add-manufacture-adapter r1
- Added internal sealed `ManufactureGiftPackageBomAdapter : IGiftPackageBomSource` mapping `ProductPart` -> `LogisticsGiftPackagePart`.
- Registered in `ManufactureModule` (scoped) next to the other cross-module adapters.
- Added 4 tests (mapping/order, empty, exception propagation, arg forwarding); all pass.
