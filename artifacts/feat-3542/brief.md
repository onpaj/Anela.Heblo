# [E2E] Transport Box pages fail to render (create/receive/manage) — 18 failing tests

## Summary

In the nightly E2E regression run [#191](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966) (branch `main`, commit `738a99c`), **18 tests across the `transport` module failed** because the Transport Box pages don't render their expected elements within the timeout.

## Root cause signature

Multiple related "element not found" timeouts on the transport box screens:

```
Error: expect(locator).toBeVisible() failed / element(s) not found
  - waiting for locator('main, [role="main"]')                              (box-receive)
  - waiting for locator('button').filter({ hasText: /Otevřít nový box/ })   (box-creation / boxes-basic)
  - waiting for locator('h1')                                               (box-management / boxes-basic)
```

The primary page containers and the "Otevřít nový box" action never appear, so navigation, creation, receiving, and detail workflows all fail. Note: some transport specs (box-items, box-workflow, ean-integration) fully passed, so the failures are concentrated in the list/create/receive/management entry points rather than the whole module.

## Affected specs (failing test count)

| Spec | Failures |
|------|---------:|
| transport/box-receive.spec.ts | 6 |
| transport/box-creation.spec.ts | 5 |
| transport/boxes-basic.spec.ts | 4 |
| transport/box-management.spec.ts | 3 |
| **Total** | **18** |

## Environment

- Workflow: 🎭 E2E Nightly Regression Tests, run #191
- Target: `https://heblo.stg.anela.cz` (staging)
- Screenshots/video artifacts: `e2e-failure-screenshots-all-191`, `e2e-test-results-all-191` on the [run page](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966).

## Suggested first steps

1. Navigate to the Transport Boxes list/create/receive pages on staging and confirm the page container (`main`), `h1`, and the `Otevřít nový box` button render.
2. Check whether these entry points share a load path that's failing while `box-items`/`box-workflow` use a different (working) one.
3. Inspect the transport box list API for errors/latency.
