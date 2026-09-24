# `_lib/gh_api.sh`'s `pr_list()` breaks with "Argument list too long"

`pr_list()` (and `req_paginate()`) accumulate JSON across a loop by passing
the growing array as a `--argjson` to `jq` on every iteration:

```sh
out=$(jq -c -n --argjson a "$out" --argjson e "$entry" '$a + [$e]')
```

This is O(n²) in the total accumulated bytes, and once enough open PRs
(with their full bodies) pile up, `$out` exceeds the process argv size
limit and `jq` fails with `Argument list too long` — the whole call
aborts, no partial result.

Hit this on 2026-09-24 running `/rework-all` (`list_candidates.sh`, which
calls `pr-list` via `gh_api.sh` when `USE_GH_API=1`, the mode used in
unattended/cloud sessions where `gh` itself is blocked). Worked around it
by reconstructing the candidate list by hand with `mcp__github__list_pull_requests`
(with `fields` excluding `body` to keep responses small) plus per-PR
`pull_request_read`.

Not fixed here — this is shared infra used by `hygiene-all`, `automerge-all`,
etc. too, so a fix belongs in its own PR, not bundled into a rework-all run.
Fix would be to build the array by streaming into `jq -s` from a file/pipe
instead of re-passing the whole accumulator as an argv string each iteration.
