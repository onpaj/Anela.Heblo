# dotnet build/test can hang: stale nodeReuse servers + AccessMatrixGen crash

**Symptom:** `dotnet build`/`dotnet test` on the backend solution appears to hang
indefinitely (no new output, zero CPU growth across the `dotnet`/`MSBuild.dll`/
`VBCSCompiler` process tree) shortly after the line `Generating access matrix
artifacts...` and the `Anela.Heblo.AccessMatrixGen` tool's unhandled
`JsonException` (`'/' is an invalid start of a value`) in the `Anela.Heblo.API`
build.

**Root cause (two combined issues):**
1. `Anela.Heblo.API.csproj`'s `GenerateAccessMatrix` target (Debug-only,
   `BeforeTargets="Build"`) invokes `Anela.Heblo.AccessMatrixGen` with only 3
   args (ts path, json path, cs path); the tool's `Program.cs` expects the
   *first* arg to be the manifest path, so it always crashes with a
   `JsonException`. This is `ContinueOnError="true"`, so it does **not** by
   itself fail the build — it just logs an MSB3073 warning and continues.
2. In this sandbox, if stale `nodeReuse:true` MSBuild server processes survive
   from an earlier `dotnet` invocation in the session, a build that hits the
   above crash can leave the surviving MSBuild worker nodes deadlocked
   (all `futex_do_wait`, zero CPU) instead of continuing past the
   `ContinueOnError` step. Killing the hung processes and retrying seems to
   work but is not reliable — the deadlock recurs on the next invocation if
   stale nodes are still around.

**Fix that reliably avoids it:** shut down build servers first, then build/test
with node reuse disabled:

```bash
dotnet build-server shutdown
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 \
  dotnet test <csproj> --filter "..." -nodeReuse:false
```

With node reuse off, the build still hits and logs the same
`GenerateAccessMatrix`/`AccessMatrixGen` crash (harmless, `ContinueOnError`)
but reliably continues to completion instead of hanging.

**If you hit a real hang:** check `ps aux | grep -E "dotnet|VBCS"` — if every
process in the tree is `S` (sleeping) on `futex_do_wait` in `/proc/<pid>/wchan`
with CPU time frozen across repeated checks (not just slow), it is this
deadlock, not just a slow build. `kill -9` the stuck `dotnet test`/`MSBuild.dll`
tree, run `dotnet build-server shutdown`, and retry with the env vars above.

Do not "fix" this by silencing or removing the `AccessMatrixGen` crash itself
without checking with the owner first — the argument-order bug in
`Anela.Heblo.API.csproj`'s `GenerateAccessMatrix` target vs.
`Anela.Heblo.AccessMatrixGen/Program.cs`'s expected arg order looks like a
separate, pre-existing bug worth its own fix, orthogonal to this workaround.

**Confirmed again 2026-08-29** (issue #3974, PR #3984): the documented fix
above (`dotnet build-server shutdown` + `DOTNET_CLI_DISABLE_BUILD_SERVERS=1
MSBUILDDISABLENODEREUSE=1 ... -nodeReuse:false`, without also disabling
shared compilation) was **not sufficient on its own** this time — `dotnet
test` still deadlocked at the identical point (confirmed via
`/proc/<pid>/wchan` = `futex_do_wait` on every process in the tree, frozen
`ps %cpu` decaying over repeated samples) even with `-m:1 -nodeReuse:false`
plus the `MSBUILDDISABLENODEREUSE=1` env var alone, across two separate
attempts. What finally worked was adding `-p:UseSharedCompilation=false` on
top of the existing flags:

```bash
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet test <csproj> --filter "..." -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

This makes sense given the root cause: the `GenerateAccessMatrix` target's
`<Exec>` spawns a **nested** `dotnet run --project .../AccessMatrixGen`
from inside the outer build. That nested process gets its own MSBuild node,
but by default it still tries to reuse the *same* VBCSCompiler (Roslyn
shared compilation) pipe as the outer build — `-nodeReuse:false` alone only
stops MSBuild *node* reuse, not the separate VBCSCompiler server. Disabling
`UseSharedCompilation` forces each nested/outer build to spin up its own
`csc.dll` process instead of contending over one shared compiler pipe,
which is what actually broke the deadlock this time. If the previously
undocumented flag combo still doesn't get you unstuck, add
`-p:UseSharedCompilation=false` before concluding the environment is
unfixable.
