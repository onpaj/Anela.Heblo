### task: photobank-phototag-schema-regression-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`

- [ ] **Step 1: Write the failing (currently-nonexistent) test**

Add a new test class to the bottom of `PhotoSchemaTests.cs` (same file, reuse the existing
`NewNpgsqlContext()` private helper already defined in `PhotoSchemaTests`):

```csharp
[Theory]
[InlineData(nameof(PhotoTag.CreatedAt))]
public void PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone(string propertyName)
{
    using var db = NewNpgsqlContext();

    var property = db.Model
        .FindEntityType(typeof(PhotoTag))!
        .FindProperty(propertyName)!;

    property.GetColumnType().Should().Be(
        "timestamp",
        $"{propertyName} stores UTC and must map to 'timestamp without time zone' to match the " +
        "global UTC->Unspecified converter; 'timestamp with time zone' rejects Unspecified writes");
}
```

Add this as a new `[Theory]` method inside the existing `PhotoSchemaTests` class (do not create a
new class — `PhotoTag` is in the same `Anela.Heblo.Domain.Features.Photobank` namespace already
`using`'d at the top of this file).

- [ ] **Step 2: Run test to verify it passes immediately**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotoSchemaTests.PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone"`

Expected: PASS immediately — `PhotoTagConfiguration.cs` already calls `.AsUtcTimestamp()` on
`CreatedAt` (verified during spec/arch-review research). This test is a **regression guard**, not a
fix — it exists so a future change that removes that mapping fails CI immediately, the same
protective role `PhotoSchemaTests`'s existing theories already play for `Photo` and
`PhotobankIndexRoot`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs
git commit -m "test(photobank): add PhotoTag.CreatedAt to the timestamp-without-timezone regression guard"
```

---
