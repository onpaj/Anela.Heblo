### task: add-mapping-profile-tests
**Files:** create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityMappingProfileTests.cs`.

- [ ] Create class `DataQualityMappingProfileTests` (namespace `Anela.Heblo.Tests.Features.DataQuality`) building the mapper with `new MapperConfiguration(cfg => cfg.AddProfile<DataQualityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper()` (see `Features/Catalog/CatalogMappingProfileStockPriceTests.cs` for imports).
- [ ] Helper `Map(InvoiceMismatchType t)` using `InvoiceDqtResult.Create(Guid.NewGuid(), "INV-1", t, null, null, null)` then `mapper.Map<InvoiceDqtResultDto>(...)`.
- [ ] Theory: each single flag with literal int (MissingInFlexi=1, MissingInShoptet=2, TotalWithVatDiffers=4, TotalWithoutVatDiffers=8, ItemsDiffer=16, DuplicateInvoiceCode=32) -> `MismatchFlags` equals `[flag.ToString()]` and `MismatchType` equals literal.
- [ ] Fact: compound `MissingInFlexi | TotalWithVatDiffers` -> exactly those two names in that order, `MismatchType == 5`.
- [ ] Fact: all flags ORed -> six names ascending, not containing "None".
- [ ] Fact: `None` -> empty non-null list, `MismatchType == 0`.
- [ ] Optional Fact: `DqtRun` TestType/Status/TriggerType mapped to strings (use `DqtRun.Start(...)` as in GetDqtRunsHandlerTests).
- [ ] Run `cd backend && dotnet test test/Anela.Heblo.Tests --filter DataQualityMappingProfileTests`, then `dotnet build` and `dotnet format --include <new file>`; all must pass.
- [ ] Docs: no process doc change (test-only).
- [ ] Commit.
