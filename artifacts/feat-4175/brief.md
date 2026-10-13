# [coverage-gap] Logistics/GetGiftPackageDetailHandler: exception-type-to-error-code mapping untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs`

## Coverage
Line coverage: 12.5% (filter threshold: 60%)

## What's not tested
The handler has two `catch` blocks that map exception types to different error codes: `ArgumentException` → `ErrorCodes.ValidationError`, and any other exception → `ErrorCodes.InternalServerError`. No test asserts that each exception type produces the correct distinct error code. A swap between the two codes would silently misclassify validation errors as server errors (or vice versa), affecting how the frontend displays the failure.

## Why it matters
The frontend uses the error code to decide whether to show a user-friendly validation message or a generic server error. Misclassified error codes produce confusing UX and make it harder to diagnose production failures.

## Suggested approach
Unit tests with mocked service: (1) service throws `ArgumentException` → assert `ValidationError` in response; (2) service throws any other exception → assert `InternalServerError`. ~30 min effort.
