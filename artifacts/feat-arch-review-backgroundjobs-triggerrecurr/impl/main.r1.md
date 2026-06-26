All tasks complete. Here's the summary:

---

## Implementation Complete ✅

### What was done

Fixed the `TriggerRecurringJob` feature to return three distinct error codes instead of collapsing all failures to `RecurringJobNotFound`:

| Failure condition | Before | After | HTTP |
|---|---|---|---|
| Job not registered | `RecurringJobNotFound` (1901) | `RecurringJobNotFound` (1901) | 404 |
| Job registered but disabled | `RecurringJobNotFound` (1901) ❌ | `RecurringJobDisabled` (1904) ✅ | 409 |
| Enqueue returned null | `RecurringJobNotFound` (1901) ❌ | `RecurringJobEnqueueFailed` (1905) ✅ | 500 |

### Key architectural alignment
The controller now uses `HandleResponse(response)` — consistent with `GetRecurringJobs`, `UpdateJobStatus`, and `UpdateJobCron` in the same controller. No per-endpoint `switch` was introduced.

### Test results
- **4068 passed, 0 failed** across the full suite
- 6 new/updated handler + controller tests covering all three failure branches and all three HTTP status mappings

### Follow-up required (out of scope per spec)
`TriggerJob` does not bind `ForceDisabled` from the HTTP request — the new 409 code is only reachable via in-process callers until `[FromQuery] bool forceDisabled = false` is added to the action.