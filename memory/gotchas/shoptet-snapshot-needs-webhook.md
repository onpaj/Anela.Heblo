---
name: shoptet-snapshot-needs-webhook
description: Every Shoptet /snapshot endpoint returns 403 until a job:finished webhook is registered — bulk export has to be paged instead
metadata:
  type: gotcha
---

`GET /api/orders/snapshot` answers

```
403 {"errorCode":"required-webhook-registration",
     "message":"Webhook for job:finished is not registered."}
```

Confirmed 2026-09-22 on **both** the production (269953 / anela.cz) and test (780175) stores;
`GET /api/webhooks` returns zero registered webhooks on both. The same applies to
`GET /api/products/snapshot` and `GET /api/pricelists/{id}/snapshot`.

It is **not** the 202 + pollable `jobId` contract that `GET /api/orders/history/snapshot` uses —
it refuses outright, and registering a `job:finished` webhook needs a publicly reachable endpoint
Shoptet can POST to, which Heblo does not have.

**Why:** plans that assume "use the snapshot export for the initial backfill" are unbuildable as
written. The 2026-09-22 spec for the orders mirror assumed exactly that.

**How to apply:** page it instead. Window `GET /api/orders` by `creationTimeFrom`/`creationTimeTo`
(creation time never changes, so paging is stable and the walk is resumable — the unfiltered list
is *not* ordered by creation time), then one `GET /api/orders/{code}` per order for the line
items. `orderCodes=` batches up to 50 codes but returns the list shape with no `items[]`, so it
does not help. Budget ~4 req/s: sequential is clean, 4 parallel connections (~16 req/s) draws 429s.
See [[shoptet-order-shape-traps]].
