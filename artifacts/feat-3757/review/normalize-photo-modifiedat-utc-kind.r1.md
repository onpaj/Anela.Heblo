# Code Review: normalize-photo-modifiedat-utc-kind

## Summary
The implementation matches the task-context spec exactly: `PhotobankIndexJob.cs:181` now stamps `photo.ModifiedAt` with `DateTimeKind.Utc` via `DateTime.SpecifyKind` (using `SpecifyKind`, not `ToUniversalTime`, as FR-2 requires), and the prescribed regression test was added verbatim. Built and ran the full `PhotobankIndexJobTests` fixture locally (Release config, to sidestep an unrelated pre-existing `GenerateAccessMatrix` Debug-only build-target issue in this worktree) — all 12 tests pass, including the new one.

## Review Result: PASS

### task: normalize-photo-modifiedat-utc-kind
**Status:** PASS

## Overall Notes
- Diff verified via `git show 638d0ec`: only `PhotobankIndexJob.cs:181` (Photo.ModifiedAt assignment) and the one new test in `PhotobankIndexJobTests.cs` were touched — no unrelated lines changed, matching the task's "do not change any other line" instruction and the impl summary's file list.
- Spec compliance confirmed against `spec.r1.md`'s FR-2 acceptance criteria: the exact expression `photo.ModifiedAt = item.LastModifiedAt.HasValue ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc) : DateTime.UtcNow;` is present, and `DateTime.SpecifyKind` (relabel, not shift) is used rather than `.ToUniversalTime()`.
- Test verified independently, not just trusted from the impl summary: ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" -c Release` → `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12`, matching the impl summary's claim.
- Architecture adherence: the new test copies the existing `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` Arrange/mocking pattern exactly, as instructed, and the fix itself mirrors the `MapContactDataToEntity`/Smartsupp "relabel, not shift" precedent cited in `arch-review.r1.md`.
- One nuance worth flagging (does not affect this task's PASS status, since it was already anticipated and explicitly scoped by the feature's own spec/arch-review, not something this task's author introduced): `PhotoConfiguration.cs` maps `ModifiedAt` to column type `"timestamp"` (without time zone), and `ApplicationDbContext.OnModelCreating` installs a global `ValueConverter` on every `DateTime`/`DateTime?` property that unconditionally re-stamps the value to `Kind=Unspecified` immediately before every write (and back to `Utc` on read), regardless of what `Kind` the in-memory value carried. Given that converter, this task's `SpecifyKind(Utc)` change has no effect on what Npgsql actually receives — the converter overwrites it back to `Unspecified` right before the write either way. Both `spec.r1.md` (NFR-2, "the real remediation is the already-authored pending migration") and `arch-review.r1.md`'s risk table ("FR-2's fix doesn't actually change today's exception rate ... explicitly acceptable and already scoped for ... do not treat FR-2 alone as 'the fix'") already call this out, so it's a known, accepted limitation of this specific task's scope, not a defect in it.
- Given that explicit spec framing, the implementation summary's "PR Summary" section slightly overstates the causal effect ("Fixes the recurring `System.ArgumentException`... by normalizing `Photo.ModifiedAt`") without the caveat the arch-review asked for. This is a documentation/wording nuance in the impl artifact, not a code defect — worth tightening if that summary is reused verbatim as the actual PR description, but it does not block this task's PASS.

**Status:** PASS
