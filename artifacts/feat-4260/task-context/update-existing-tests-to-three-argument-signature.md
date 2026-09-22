### task: update-existing-tests-to-three-argument-signature


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs:61,78,125`
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs:42,67,107`

- [ ] **Step 1: Update the two "never called" verifications in the handler tests**

In `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs`, line 61 (inside `Handle_WhenJobNotFound_ReturnsNotFoundError`) and line 78 (inside `Handle_WhenCronExpressionInvalid_ReturnsBadRequest`) are both currently:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
```

Change **both** lines to:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
```

`It.IsAny<string>()` is correct here — these assert the scheduler was **never** called, so there is no concrete value to pin.

- [ ] **Step 2: Strengthen the happy-path verification to assert the concrete forwarded time zone**

Still in `UpdateRecurringJobCronHandlerTests.cs`, line 125 (inside `Handle_WhenValidCron_UpdatesDbAndHangfire`) is currently:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron), Times.Once);
```

Change to:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron, "Europe/Prague"), Times.Once);
```

`"Europe/Prague"` is the exact literal the `CreateTestJob` helper at the bottom of this same file builds the entity with (`timeZoneId: "Europe/Prague"`, line 154). Do **not** use `It.IsAny<string>()` for the third argument here — asserting the concrete value is what proves the handler forwards the entity's `TimeZoneId` rather than some other source, and it is the mitigation for a future `cron`/`timeZoneId` transposition.

- [ ] **Step 3: Pass the metadata time zone at the three existing scheduler-test call sites**

In `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`, make these three edits. `"Europe/Prague"` is `ParityTestRecurringJob.Metadata.TimeZoneId` (line 150 of the same file), so these three tests keep asserting exactly what they asserted before.

Line 42, currently:

```csharp
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *");
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *", "Europe/Prague");
```

Line 67, currently:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", newCron);
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", newCron, "Europe/Prague");
```

Line 107, currently:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *");
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *", "Europe/Prague");
```

Do not delete, rename or weaken any assertion in these three tests. In particular `Assert.Equal("Europe/Prague", job.TimeZoneId)` on line 73 and `Assert.Equal(discoveredTimeZoneId, afterUpdate.TimeZoneId)` on line 116 stay exactly as they are.

- [ ] **Step 4: Build the whole solution — it must now succeed**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors and no new warnings. This is the first point in the plan where the full solution compiles again.

- [ ] **Step 5: Run the BackgroundJobs tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs"`

Expected: `Passed!` with 0 failed. In particular `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage`, `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration`, `UpdateCronSchedule_WithUnknownJobName_LogsWarningAndReturns` and all five `UpdateRecurringJobCronHandlerTests` must pass.

Be aware (this is expected and not a defect): after this change, `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage` and `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` pass only because the tests now hand `"Europe/Prague"` in themselves. They no longer prove that the runtime path derives the same time zone as the startup path — they prove the adapter forwards what it was given. The next two tasks restore that parity guarantee with real assertions.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
git commit -m "test(background-jobs): update ICronScheduler call sites to the three-argument signature"
```
