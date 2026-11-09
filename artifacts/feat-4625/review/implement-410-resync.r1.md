# Review: implement-410-resync r1

Code matches the arch-review snippet exactly: catch filter on Gone plus stored token, token logged by root id only, single retry with null token, non-410 errors and 410 without a token propagate. Call site replaced; `using System.Net;` added. The four 410 tests pass. Postgres-fixture failures are environmental.

**Status:** PASS
