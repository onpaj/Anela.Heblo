# Implementation: implement-batch-resolution

## What was implemented

Replaced the N+1 per-user Graph API loop in `GetAppRoleMembersAsync` with a single (or chunked) Graph `$batch` request. Instead of issuing one `GET /users/{id}` call per user, the new code groups user IDs into chunks of up to 20 and sends a single `POST /$batch` request per chunk. The batch response is parsed per-sub-response, with individual failures logged as warnings rather than aborting the whole operation (except when the entire batch request itself fails, which returns an empty list and logs an error).

A class-level constant `GraphBatchSize = 20` was also added alongside the existing `SearchResultLimit` constant.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — added `GraphBatchSize` constant; replaced Step 5 N+1 loop with Graph `$batch` chunked resolution

## Tests

N/A — tests in next task

## How to verify

1. `dotnet build /home/user/Anela.Heblo/backend/src/Anela.Heblo.Application/` — should report 0 errors
2. Run the application against staging and call an endpoint that triggers `GetAppRoleMembersAsync` (e.g. any user management listing endpoint). Observe logs — instead of N individual `GET /users/{id}` calls you should see batch POST calls.
3. Verify that users are still resolved correctly with display name and email populated.

## Notes

- Graph `$batch` supports up to 20 requests per batch (documented limit), hence `GraphBatchSize = 20`.
- If the whole batch HTTP request fails (non-2xx), the method returns an empty list and logs an error — consistent with a hard failure scenario.
- Individual sub-response failures (e.g. user not found) are logged as warnings and skipped, preserving behavior of the previous loop's `continue`.
- The resolved user ID now comes from the batch response body's `id` field rather than the input `directUserIds` set, which is slightly safer and matches the previous per-request approach where the user object's `id` was used implicitly.

## PR Summary

The user-management `GetAppRoleMembersAsync` method previously issued one HTTP request to Microsoft Graph per user to resolve display name and email — an O(N) chattiness problem. For a group with 100 members, that was 100 sequential HTTP calls.

This change groups user IDs into chunks of 20 and uses Graph's `$batch` endpoint (`POST /$batch`) to resolve all users in a chunk with a single HTTP round-trip. For 100 members this reduces Graph calls from 100 to 5. The batching is transparent to callers; the returned `List<UserDto>` shape is unchanged.

### Changes

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — added `GraphBatchSize = 20` constant; replaced N+1 per-user GET loop (Step 5) with chunked `$batch` POST loop that parses sub-responses individually

## Status

DONE
