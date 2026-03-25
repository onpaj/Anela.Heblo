Ask engineer agent: try to find oldest issue on GitHub with label = "agent".

Your goal is to read that issue, brainstorm over it, in case there are any information missing. When you have all the information, implement that issue. Goal is to have clean implementation with test coverage (critical paths, there is no point in having unnecessary high code coverage)

After implementation, ensure all tests are passing (BE and FE), run linters, push fix to github and create MR to main for it. Use conventional commits to handle app versioning properly

#CRITICAL!
- always update main branch from origin and create the new feature branch from it
- always create new git worktree for that issue
- use subagents workflow to implement (superpowers:executing-plans skill)
- code can be pushed to branch only when ALL tests are passing (both BE and FE).
- create detailed implementation plan from that feature
- always add "@claude" at the end of your last commit message (right before creating a PR)
