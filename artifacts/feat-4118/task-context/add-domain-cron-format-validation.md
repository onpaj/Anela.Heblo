### task: add-domain-cron-format-validation

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs:57-138`
- Test: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these three test methods to the end of the `RecurringJobConfigurationTests` class in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`, immediately before the final closing `}` of the class (after the existing `Disable_ShouldThrowValidationException_WhenModifiedByIsEmpty` test):

```csharp
    [Fact]
    public void Constructor_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange & Act & Assert
        Assert.Throws<ValidationException>(() => new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "not-a-cron",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void UpdateConfiguration_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.UpdateConfiguration(
            displayName: "Updated Job",
            description: "Updated description",
            cronExpression: "not-a-cron",
            timeZoneId: "Europe/Prague",
            modifiedBy: "admin",
            modifiedAt: DateTime.UtcNow
        ));

        // CronExpression must remain unchanged after the throw
        Assert.Equal("0 0 * * *", config.CronExpression);
    }

    [Fact]
    public void UpdateCronExpression_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.UpdateCronExpression("not-a-cron", "admin", DateTime.UtcNow));

        // CronExpression must remain unchanged after the throw
        Assert.Equal("0 0 * * *", config.CronExpression);
    }

    [Theory]
    [InlineData("0 2 * * *")]           // 5-field standard, existing test fixture value
    [InlineData("*/15 * * * *")]        // 5-field standard, from RagFeatureOptions default
    [InlineData("0 6,18 * * *")]        // 5-field standard, from MetaAdsInvoiceImportJob
    [InlineData("15 6,18 * * *")]       // 5-field standard, from GoogleAdsInvoiceImportJob
    [InlineData("0 * * * *")]           // 5-field standard, from CompleteDeliveredOrdersJob
    [InlineData("0 0 0 * * *")]         // 6-field Quartz-style (leading seconds)
    public void Constructor_ShouldAccept_KnownValidCronExpressions(string cronExpression)
    {
        // Arrange & Act
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: cronExpression,
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Assert
        Assert.Equal(cronExpression, config.CronExpression);
    }
```

- [ ] **Step 2: Run the tests to verify the new ones fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"`

Expected: the three `_ShouldThrowValidationException_WhenCronExpressionIsMalformed` tests FAIL (no exception is thrown today for `"not-a-cron"`); the `Constructor_ShouldAccept_KnownValidCronExpressions` theory PASSES already (no behavior change needed for valid input). All previously-existing tests in the file still PASS.

- [ ] **Step 3: Add the `ValidateCronFormat` helper and wire it into all three write paths**

In `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`, add this private static method at the end of the class, immediately before the final closing `}` (after `UpdateCronExpression`):

```csharp
    /// <summary>
    /// Structural-only check: confirms the value has the field count of a standard
    /// (5-field) or Quartz-style (6-field, leading seconds) CRON expression. Does
    /// NOT validate per-field value ranges (e.g. "99 99 * * *" passes this check) —
    /// that stronger semantic validation is performed by
    /// UpdateRecurringJobCronHandler.IsValidCronExpression (NCrontab.Advanced) on
    /// the one user-facing write path. This check exists so the entity itself
    /// cannot be put into a state with an obviously malformed CronExpression via
    /// any caller, not only the MediatR handler.
    /// </summary>
    private static void ValidateCronFormat(string cronExpression)
    {
        var fields = cronExpression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (5 or 6))
            throw new ValidationException($"'{cronExpression}' is not a valid CRON expression.");
    }
```

Then call it from all three write paths, immediately after each method's existing `IsNullOrWhiteSpace(cronExpression)` check, before any field is assigned:

In the constructor, change:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ValidationException("TimeZoneId is required");
```
to:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ValidationException("TimeZoneId is required");
```

In `UpdateConfiguration`, change:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ValidationException("TimeZoneId is required");
```
to:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ValidationException("TimeZoneId is required");
```

In `UpdateCronExpression`, change:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");
```
to:
```csharp
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");
```

No `using` changes are needed — `ValidationException` is already imported via `using System.ComponentModel.DataAnnotations;` at the top of the file.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"`

Expected: all tests in `RecurringJobConfigurationTests` PASS, including the three new malformed-input tests and the six-case valid-input theory.

- [ ] **Step 5: Run the full backend test suite and format/build gates**

Run, in order, from the repository root (`Anela.Heblo.sln` lives at the repo root, not under `backend/`):
```bash
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
```

Expected: `dotnet format` reports no changes needed; `dotnet build` succeeds with no new warnings/errors; `dotnet test` passes in full (this specifically confirms `RecurringJobSeeder`'s use of the constructor and `UpdateConfiguration` with real `RecurringJobMetadata.CronExpression` values from all `IRecurringJob` implementations — all standard 5-field expressions — still succeeds, and that `UpdateRecurringJobCronHandler`'s existing tests, if any, are unaffected).

If `dotnet format` reports changes, apply them (`dotnet format Anela.Heblo.sln`) and re-run `--verify-no-changes` before proceeding.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs
git commit -m "$(cat <<'EOF'
fix(background-jobs): enforce CRON format validation in RecurringJobConfiguration domain entity

RecurringJobConfiguration's constructor, UpdateConfiguration, and
UpdateCronExpression previously accepted any non-empty string as a CRON
expression -- the only structural check lived in
UpdateRecurringJobCronHandler, an Application-layer concern reachable
through only one write path. Add a private static ValidateCronFormat
helper (5- or 6-field structural check, no new dependency) and call it
from all three write paths so the domain entity itself enforces the
invariant regardless of caller.

Closes #4118

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_012wQYydwH3QnaXDteHttpNT
EOF
)"
```
