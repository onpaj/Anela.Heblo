## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (`MetaAdsTransactionSource.cs` and
`MetaAdsTransactionSourceTests.cs`) against `spec.r1.md`.

- FR-1: `BuildInitialUrl` no longer appends `access_token`; `FetchPageAsync`
  now builds an explicit `HttpRequestMessage` with
  `Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken)`
  for every call it makes — including pagination follow-ups, since `url`
  (whether the initial URL or `page.Paging?.Next`) always flows through the
  same `FetchPageAsync` method. Verified via
  `GetTransactionsAsync_FollowsPagination_UntilNoNextCursor`, which asserts
  no captured URL contains `access_token=` and both captured requests carry
  the expected `Authorization: Bearer test-token` header.
- FR-2: `RedactToken` and its call site are fully removed; the debug log
  now logs the raw URL directly, which is safe since the URL never
  contains the token. `grep` confirms no remaining reference to
  `RedactToken` or `access_token` anywhere under
  `Anela.Heblo.Adapters.MetaAds`. (An unrelated `RedactToken` helper exists
  in the separate `ShoptetApi` adapter — out of scope for this issue, left
  untouched as expected.)
- FR-3: Resilience pipeline (`_pipeline.ExecuteAsync`), `EnsureSuccessStatusCode()`,
  and the null-response `InvalidOperationException` are all unchanged.
- NFR-2: The Authorization header value itself is never logged (only the
  URL and method), matching "must not be logged anywhere in plaintext".

Verification performed:
- `dotnet build` on `Anela.Heblo.Adapters.MetaAds.csproj` — 0 errors.
- `dotnet test --filter FullyQualifiedName~MetaAdsTransactionSourceTests` —
  7/7 passed.

No correctness bugs found. No advisory cleanups worth flagging — the diff
is minimal and directly scoped to the spec.
