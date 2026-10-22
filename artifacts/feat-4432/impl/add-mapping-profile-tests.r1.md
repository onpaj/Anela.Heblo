# add-mapping-profile-tests r1

Created `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityMappingProfileTests.cs` with a theory over the six single flags, compound (5), all-flags (six names, no "None"), None (empty list, 0), and a DqtRun enum-to-string fact.

Verification: `dotnet test --filter DataQualityMappingProfileTests` -> 10 passed; `dotnet format --verify-no-changes` on the file clean. Test-only; no process doc change.
