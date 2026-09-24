# Cloud session argv limit is far below the OS-reported ARG_MAX

In this repo's cloud/scheduled execution environment, `getconf ARG_MAX`
reports 2097152 (2MB) but the *actual* enforced limit for a single `exec`
(e.g. a `jq --argjson x "$var"` call) is roughly 130KB. Passing a GitHub API
response — a page of PR comments, a CI check-runs payload, an
accumulating PR list — as a jq command-line argument fails with `bash:
/usr/bin/jq: Argument list too long` well before the reported limit, and
well below sizes that look "small" (a 59-comment PR thread was ~360KB and
failed).

Hit this via `.claude/skills/rework-pr/list_candidates.sh` →
`.claude/skills/_lib/gh_api.sh`'s `req_paginate` (comment pagination),
`pr_list` (candidate accumulator), and `pr_view`'s CI-rollup fetch — all of
which built up JSON via `jq --argjson`. Fixed by writing the JSON to a temp
file and using `jq --slurpfile name file` (`$name[0]` to unwrap) instead of
putting it on the command line. See `_jq_file()` in `gh_api.sh`.

Any new code in this repo's `.claude/skills/_lib/*.sh` (or similar
automation scripts) that pipes a growing/raw GitHub API response through
`jq --argjson`/`--arg` should use this same temp-file pattern rather than
assuming argv can hold "a few hundred KB safely" — it can't, here.
