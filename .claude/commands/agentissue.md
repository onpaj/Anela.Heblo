## Step 1: Check open PRs with "agent" label first

Before picking a new issue, check GitHub for any open PRs that have the "agent" label. For each such PR:
- Check for merge conflicts with main — resolve them if present
- Check CI/test status — fix any failing tests
- Get the PR into a mergeable state (all tests passing, no conflicts)
- Merge it if ready, or leave it ready-to-merge before proceeding

Only after all "agent"-labelled PRs are clean, proceed to pick a new issue.

## Step 2: Implement the oldest open issue with label "agent"

Find the oldest issue on GitHub with label = "agent" that does NOT already have an open PR.

Read that issue, brainstorm over it — if any information is missing, gather it. When you have all the information, implement that issue. Goal is clean implementation with test coverage (critical paths; no unnecessary high code coverage).

After implementation, ensure all tests are passing (BE and FE), run linters, push to GitHub and create a PR to main. Use conventional commits to handle app versioning properly.

#CRITICAL!
- always update main branch from origin and create the new feature branch from it
- always create new git worktree for that issue
- use subagents workflow to implement (superpowers:executing-plans skill)
- code can be pushed to branch only when ALL tests are passing (both BE and FE).
- create detailed implementation plan from that feature
- always add "@claude" at the end of your last commit message (right before creating a PR)
