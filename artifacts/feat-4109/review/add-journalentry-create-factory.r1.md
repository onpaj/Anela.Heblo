# Code Review: add-journalentry-create-factory

## Summary
The implementation adds a `JournalEntry.Create()` static factory method that centralizes construction-time normalization (trimming, date normalization, audit-field stamping) inside the aggregate, matching the existing `Update()` pattern and preventing bypasses at call sites. All 4 required tests are present, correctly placed, and verify the key invariants.

## Review Result: PASS

### task: add-journalentry-create-factory
**Status:** PASS

**Verification:**

1. **Spec Compliance — PASS**
   - ✓ Create() static factory added with correct signature: `public static JournalEntry Create(string title, string content, DateTime entryDate, string userId, string username, DateTime now)`
   - ✓ Inserted directly before `Update()` method (line 151-170 in diff)
   - ✓ All 4 required tests present and correctly named:
     - `Create_TrimsTitleAndContentAndNormalizesEntryDate` — tests trimming and `.Date` normalization, including TimeOfDay verification
     - `Create_StampsCreatedAndModifiedAuditFieldsFromSuppliedNow` — verifies CreatedAt and ModifiedAt both receive the supplied `now` value, plus CreatedByUserId/CreatedByUsername
     - `Create_LeavesModificationAndDeletionAuditFieldsNull` — verifies ModifiedByUserId/ModifiedByUsername are null, IsDeleted is false, and all deletion audit fields are null
     - `Create_ReturnsEntryWithEmptyProductAndTagCollections` — verifies both collections are initialized as non-null empty List
   - ✓ Tests inserted before `// ----- Update -----` section (line 219 in original, now line 294 after insert)
   - ✓ Conventional commit message: `feat(journal): add JournalEntry.Create() factory for construction-time normalization`

2. **Architecture Adherence — PASS**
   - ✓ Construction-time normalization occurs inside the aggregate (not delegated to handlers or factories)
   - ✓ Implementation mirrors `Update()` normalization pattern:
     - Title/Content: trimmed via `.Trim()`
     - EntryDate: normalized to date-only via `.Date`
   - ✓ Audit-field stamping is explicit: CreatedAt and ModifiedAt both set to supplied `now`, CreatedByUserId/CreatedByUsername set from params
   - ✓ Correctly distinguishes creation audit fields from modification fields: CreatedBy* are set; ModifiedBy* intentionally left null
   - ✓ Collections (ProductAssociations, TagAssignments) inherit default empty List initialization from class field definitions
   - ✓ Static factory on aggregate, not external factory class — correct pattern for this codebase

3. **Test Design — PASS**
   - ✓ Test 1 verifies normalization: title/content trimming and date component extraction (`.Date` and `.TimeOfDay` checks)
   - ✓ Test 2 verifies audit stamping: CreatedAt and ModifiedAt both equal the supplied `now` parameter; CreatedByUserId and CreatedByUsername are correctly assigned
   - ✓ Test 3 verifies null-field defaults: ModifiedByUserId/ModifiedByUsername are null (correct for a new entry), IsDeleted false, all deletion audit fields null
   - ✓ Test 4 verifies collection initialization: both ProductAssociations and TagAssignments are present, not null, and empty
   - ✓ Tests use AAA pattern with clear Arrange-Act-Assert structure
   - ✓ Tests use FluentAssertions idiomatically (`.Should().Be()`, `.Should().BeNull()`, `.Should().BeEmpty()`)
   - ✓ Tests follow existing naming convention (MethodUnderTest_Scenario)

4. **Correctness — PASS**
   - ✓ No logic errors visible in the factory implementation
   - ✓ Object-initializer syntax for setting audit fields is valid (JournalEntry is a regular class with no constraints that would block this)
   - ✓ Property assignment order (initialize object, then assign Title/Content/EntryDate) is correct and matches the pattern
   - ✓ All assertions in tests are meaningful and test distinct invariants (not redundant)
   - ✓ Developer notes confirm pre-implementation compile failure (CS1739 from Roslyn's named-argument handling) and post-implementation green: 4 new tests pass, 21 existing tests unaffected (25 total)

5. **Completeness — PASS**
   - ✓ Both required files modified: JournalEntry.cs and JournalEntryTests.cs
   - ✓ Surgical changes — only the factory and tests added, no refactoring of Update/SoftDelete or handler call-sites
   - ✓ No extraneous changes or scope creep
   - ✓ Out-of-scope task (wiring handlers to call Create()) correctly left for future work

## Overall Notes
- The distinction in Test 3 between set CreatedBy* fields and null ModifiedBy* fields is architecturally important and correctly tested.
- The `now` parameter injection into Create() enables deterministic testing and is aligned with the codebase's testing patterns.
- Commit message accurately describes the change and its purpose (construction-time normalization inside the aggregate).
