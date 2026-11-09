# Implementation: add-410-resync-tests

## What was implemented
Four tests in PhotobankIndexJobTests covering the Graph 410 Gone delta-token resync behaviour, plus a small arrange helper (SetupRootForGoneTests).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` - 4 new tests, `using System.Net;`

## Tests
- Graph410WithStoredDeltaLink_RetriesWithoutTokenAndPersistsNewDeltaLink (FAILS until task 2, as required)
- Graph410WithoutStoredDeltaLink_DoesNotRetry (passes)
- NonGoneHttpErrorWithStoredDeltaLink_DoesNotRetryOrClearToken (passes)
- Graph410OnRetry_StopsAfterTwoCallsAndDoesNotThrow (FAILS until task 2: needs a second call)

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter PhotobankIndexJobTests` - 15 pass, 2 expected failures (the two retry tests).

## Notes
Task spec said only the first test must fail; the fourth also fails pre-implementation because it asserts exactly two calls. Both pass once retry logic exists.

## PR Summary
Adds tests for the 410 Gone delta-link resync in the Photobank index job.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` - four new tests

## Status
DONE
