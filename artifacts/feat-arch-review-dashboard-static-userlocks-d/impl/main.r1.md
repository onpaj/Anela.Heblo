I'm in a read-only permission mode. The session needs write permission granted before I can create any files.

**To unblock this task**, please approve the write permission when Claude Code prompts you — or run this in your terminal to add it to settings:

```bash
cd /home/rem/sandbox/Anela.Heblo/.worktrees/feat-arch-review-dashboard-static-userlocks-d

# Add write permissions to the project settings
cat > .claude/settings.json << 'EOF'
{
  "enabledPlugins": {
    "ralph-loop@claude-plugins-official": true,
    "superpowers@claude-plugins-official": true
  },
  "permissions": {
    "allow": [
      "Write",
      "Edit",
      "Bash(dotnet build *)",
      "Bash(dotnet test *)",
      "Bash(dotnet format *)",
      "Bash(dotnet restore *)",
      "Bash(dotnet add *)",
      "Bash(git add *)",
      "Bash(git commit *)",
      "Bash(mkdir *)"
    ]
  }
}
EOF
```

Once you've done that (or clicked "Allow always" on the Write permission prompt), I can proceed with implementing all the code changes.

Here's what I'll implement once unblocked:

1. **`IKeyedAsyncLock.cs`** + **`KeyedAsyncLock.cs`** — new internal concurrency primitive in `Services/Concurrency/`
2. **`DashboardOptions.cs`** — add `UserLockSlidingExpirationMinutes` (default 10)
3. **`Anela.Heblo.Xcc.csproj`** — add `Microsoft.Extensions.Caching.Memory 8.0.1` + `InternalsVisibleTo`
4. **`XccModule.cs`** — register `IKeyedAsyncLock` singleton
5. **`DashboardService.cs`** — replace static dict with `await using (await _lockPool.AcquireAsync(...))`
6. **`DashboardServiceTests.cs`** — update constructor to pass `KeyedAsyncLock`
7. **`KeyedAsyncLockTests.cs`** — new test file for FR-1/FR-2/FR-3