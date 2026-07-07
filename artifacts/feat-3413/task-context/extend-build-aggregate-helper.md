### task: extend-build-aggregate-helper

Extend the private `BuildAggregate` helper so the three new tests can pass a `ProductType`
and omit `monthlyKeys`. The two existing callers pass `monthlyKeys` positionally, so adding
optional parameters at the end preserves them.

**Files:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

1. Open the file. Locate the current `BuildAggregate` signature at line 100:

   ```csharp
   private static CatalogAggregate BuildAggregate(string productCode, IEnumerable<DateTime> monthlyKeys)
   ```

2. Replace the signature and body with:

   ```csharp
   private static CatalogAggregate BuildAggregate(
       string productCode,
       IEnumerable<DateTime>? monthlyKeys = null,
       ProductType type = ProductType.Product)
   {
       var aggregate = new CatalogAggregate
       {
           Id = productCode,
           ProductName = "Test Product",
           Type = type
       };

       foreach (var key in monthlyKeys ?? Enumerable.Empty<DateTime>())
       {
           aggregate.Margins.MonthlyData[key] = new MarginData();
       }

       return aggregate;
   }
   ```

3. Build the test project to confirm no compilation errors:

   ```
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected: `Build succeeded.`

4. Commit:
   ```
   git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
   git commit -m "test: extend BuildAggregate helper with optional type and monthlyKeys params"
   ```
